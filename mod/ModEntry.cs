using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Android.App;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Hardware.Display;
using Android.Views;
using Android.Widget;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using Stopwatch = System.Diagnostics.Stopwatch;
using XColor = Microsoft.Xna.Framework.Color;

namespace DualScreen;

/// <summary>Hosts the bottom-screen window: draws panels offscreen with the game's own
/// SpriteBatch, copies the pixels into an Android ImageView, and routes touches back.</summary>
public class ModEntry : Mod
{
    // Half the bottom display (1240x1080); shown at 2x with no filtering so pixel art stays crisp.
    public const int W = 620, H = 540;

    Activity activity;
    Presentation window;
    ImageView view;
    readonly RenderTarget2D[] targets = new RenderTarget2D[2];
    SpriteBatch batch;
    Panels panels;
    readonly byte[][] bytes = { new byte[W * H * 4], new byte[W * H * 4] };
    readonly Bitmap[] bitmaps = new Bitmap[2];
    readonly BitmapDrawable[] drawables = new BitmapDrawable[2];
    readonly Java.Nio.ByteBuffer[] pixels = new Java.Nio.ByteBuffer[2];
    readonly ConcurrentQueue<(MotionEventActions action, int x, int y)> touches = new();
    int flip, frames;
    int pendingRedraws, lastKey;
    long drawTicks, readTicks;
    readonly Stopwatch timer = new();

    public override void Entry(IModHelper helper)
    {
        panels = new Panels(helper, Monitor);
        diagnostics = panels.diagnostics = helper.ReadConfig<ModConfig>().Diagnostics;
        helper.Events.GameLoop.GameLaunched += (_, _) => OpenWindow();
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.GameLoop.DayStarted += (_, _) => panels.MarkCropsDirty();
        helper.Events.World.TerrainFeatureListChanged += (_, _) => panels.MarkCropsDirty();
        helper.Events.World.ObjectListChanged += (_, _) => panels.MarkCropsDirty();
        // Picking a crop by hand isn't a tool swing, but it lands in the bag.
        helper.Events.Player.InventoryChanged += (_, _) => panels.MarkCropsDirty();
    }

    void OpenWindow()
    {
        activity = Microsoft.Xna.Framework.Game.Activity;
        if (activity == null) { Monitor.Log("No Android activity; bottom screen disabled.", LogLevel.Warn); return; }

        activity.RunOnUiThread(() =>
        {
            try
            {
                var dm = (DisplayManager)activity.GetSystemService(Android.Content.Context.DisplayService);
                var displays = dm.GetDisplays(DisplayManager.DisplayCategoryPresentation);
                if (displays.Length == 0) { Monitor.Log("No second screen found.", LogLevel.Warn); return; }

                // Before the view exists: Render posts to these as soon as it sees the view.
                ui = new Android.OS.Handler(Android.OS.Looper.MainLooper!);
                pushFrame = new Java.Lang.Runnable(PushFrame);
                exitCheck = new Java.Lang.Runnable(CheckExit);
                window = new Presentation(activity, displays[0]);
                // Never take key focus: the pad must keep driving the game.
                window.Window.AddFlags(WindowManagerFlags.NotFocusable);
                view = new ImageView(activity);
                view.SetScaleType(ImageView.ScaleType.FitXy);
                view.SetBackgroundColor(Android.Graphics.Color.Black);
                view.Touch += (_, e) =>
                {
                    // Consumed here, so the game never sees it and keeps its controller prompts.
                    e.Handled = true;
                    if (view.Width > 0)
                        touches.Enqueue((e.Event.ActionMasked, (int)(e.Event.GetX() * W / view.Width), (int)(e.Event.GetY() * H / view.Height)));
                };
                window.SetContentView(view);
                window.Show();
                ui.PostDelayed(exitCheck, 500);
            }
            catch (Exception ex) { Monitor.Log($"Couldn't open the bottom screen: {ex}", LogLevel.Error); }
        });

        // Hide while the app is paused or the Thor sleeps, so the panel never keeps it awake.
        Microsoft.Xna.Framework.AndroidGameActivity.Paused += (_, _) => window?.Hide();
        Microsoft.Xna.Framework.AndroidGameActivity.Resumed += (_, _) => window?.Show();
    }

    /// <summary>The game is quitting (title screen Exit): close the bottom window too, or it outlives the game.</summary>
    void CloseWindow()
    {
        if (window == null) return;
        var w = window;
        window = null;
        view = null;
        Monitor.Log("Game is exiting; closing the bottom screen.", LogLevel.Trace);
        activity.RunOnUiThread(() => w.Dismiss());
        // Title-screen Exit only closes the game's screen; Cinderbox keeps the process (and its music) running.
        // Nothing is in progress on the title, so end the app once SMAPI has had a moment to flush its log.
        if (Game1.quit)
        {
            Monitor.Log("Exit pressed; ending the app.", LogLevel.Trace);
            // A plain background timer: Android's main thread stops running posted work once the game screen closes.
            System.Threading.Tasks.Task.Delay(1500).ContinueWith(_ => Android.OS.Process.KillProcess(Android.OS.Process.MyPid()));
        }
    }

    /// <summary>The game loop stops when the game exits, so also watch from Android's side: once the game's
    /// activity is finishing, close the bottom window.</summary>
    void CheckExit()
    {
        if (window == null) return;
        if (activity.IsFinishing || activity.IsDestroyed) { CloseWindow(); return; }
        ui.PostDelayed(exitCheck, 500);
    }

    // One Java Runnable each, made once: a new one per frame (RunOnUiThread) meant ~30 new Java objects a
    // second for the garbage collector to cross-check, which costs pauses.
    Android.OS.Handler ui;
    Java.Lang.Runnable pushFrame, exitCheck;
    volatile int readyFlip = -1;

    /// <summary>UI thread: copy the latest read-back frame into its bitmap and show it.</summary>
    void PushFrame()
    {
        var v = view;
        int f = readyFlip;
        if (v == null || f < 0) return;
        var buf = bytes[f];
        var direct = pixels[f] ??= Java.Nio.ByteBuffer.AllocateDirect(buf.Length);
        Marshal.Copy(buf, 0, Android.Runtime.JNIEnv.GetDirectBufferAddress(direct.Handle), buf.Length);
        direct.Rewind();
        var bmp = bitmaps[f] ??= Bitmap.CreateBitmap(W, H, Bitmap.Config.Argb8888);
        bmp.CopyPixelsFromBuffer(direct);
        v.SetImageDrawable(drawables[f] ??= NewDrawable(bmp));
        v.Invalidate();
    }

    void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
    {
        if (view == null) return;
        if (!diagnostics) { Update(e); return; }
        long a0 = GC.GetAllocatedBytesForCurrentThread(), t0 = Stopwatch.GetTimestamp();
        try { Update(e); }
        finally
        {
            modBytes += GC.GetAllocatedBytesForCurrentThread() - a0;
            modTicks += Stopwatch.GetTimestamp() - t0;
        }
    }

    bool diagnostics;
    long modBytes, modTicks;
    int renders, keyChanges;

    void Update(UpdateTickedEventArgs e)
    {
        if (Game1.quit) { CloseWindow(); return; }
        if (diagnostics) TrackFrame();
        panels.Tick();

        while (touches.TryDequeue(out var t))
        {
            panels.Touch(t.action, t.x, t.y);
            // Two renders: readback lags one frame behind the draw.
            pendingRedraws = 2;
        }

        if (pendingRedraws > 0) { pendingRedraws--; Render(); return; }
        // Redraw as soon as what the screen shows changes (two renders, as readback lags a frame): checked
        // every other tick so switching items with the triggers tracks smoothly, even on animated screens.
        if (e.IsMultipleOf(2))
        {
            int key = panels.StateKey();
            if (key != lastKey)
            {
                lastKey = key;
                keyChanges++;
                pendingRedraws = 1;
                Render();
                return;
            }
        }
        int interval = panels.AnimationInterval;
        if (interval > 0 ? e.IsMultipleOf((uint)interval) : e.IsMultipleOf(600)) Render();
    }

    void Render()
    {
        var gd = Game1.graphics.GraphicsDevice;
        batch ??= new SpriteBatch(gd);
        // Draw into one target while reading back the other: the previous frame is already
        // finished on the GPU, so the readback doesn't stall on this frame's draws.
        flip ^= 1;
        var draw = targets[flip] ??= new RenderTarget2D(gd, W, H);
        var read = targets[flip ^ 1] ??= new RenderTarget2D(gd, W, H);

        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        timer.Restart();
        var oldTargets = gd.GetRenderTargets();
        var oldViewport = gd.Viewport;
        gd.SetRenderTarget(draw);
        gd.Clear(new XColor(0, 0, 0));
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        try { panels.Draw(batch); }
        catch (Exception ex) { Monitor.LogOnce($"Panel draw failed: {ex}", LogLevel.Error); }
        batch.End();
        gd.SetRenderTargets(oldTargets);
        gd.Viewport = oldViewport;
        long drawn = timer.Elapsed.Ticks;

        int f = flip;
        var buf = bytes[f];
        read.GetData(buf);
        long readDone = timer.Elapsed.Ticks;

        // The Java-side copy runs on the UI thread so the game thread only pays for draw + readback.
        // Each flip has its own byte array, bitmap and drawable, so the next readback can't race it.
        readyFlip = f;
        ui.Post(pushFrame);

        drawTicks += drawn; readTicks += readDone - drawn;
        allocBytes += GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        maxUpdate = Math.Max(maxUpdate, readDone);
        maxDraw = Math.Max(maxDraw, drawn);
        maxRead = Math.Max(maxRead, readDone - drawn);
        renderedThisTick = true;
        renders++;
        if (diagnostics && ++frames % 300 == 0)
        {
            Monitor.Log($"Bottom screen ms/update: draw {drawTicks / frames / 1e4:0.00}, read {readTicks / frames / 1e4:0.00}, worst {maxUpdate / 1e4:0.0} (draw {maxDraw / 1e4:0.0}, read {maxRead / 1e4:0.0}) on {panels.ShowingName}; mod allocates {allocBytes / frames / 1024.0:0.0} KB/update", LogLevel.Trace);
            drawTicks = readTicks = frames = 0;
            allocBytes = 0;
            maxUpdate = maxDraw = maxRead = 0;
        }
    }

    // Stutter diagnostics: every game frame over 50 ms (with what was in it), and the game's allocation rate.
    readonly Stopwatch frameClock = Stopwatch.StartNew(), rateClock = Stopwatch.StartNew();
    long maxUpdate, maxDraw, maxRead, allocBytes, rateBytes;
    bool renderedThisTick;
    int slowFrames, lastG0, lastG1, lastG2, rateG0, rateG1, rateG2;

    void TrackFrame()
    {
        double ms = frameClock.Elapsed.TotalMilliseconds;
        frameClock.Restart();
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        if (ms > 50)
        {
            slowFrames++;
            string gc = g2 != lastG2 ? "full" : g1 != lastG1 ? "gen1" : g0 != lastG0 ? "gen0" : "none";
            Monitor.Log($"Slow frame {ms:0} ms on {panels.ShowingName}: bottom-screen update {(renderedThisTick ? "yes" : "no")}, GC {gc}", LogLevel.Trace);
        }
        lastG0 = g0; lastG1 = g1; lastG2 = g2;
        renderedThisTick = false;

        if (rateClock.Elapsed.TotalSeconds >= 10)
        {
            long total = GC.GetTotalAllocatedBytes();
            double secs = rateClock.Elapsed.TotalSeconds;
            Monitor.Log($"[{panels.ShowingName}] mod: {modBytes / secs / 1024:0.0} KB/s, {modTicks * 1000.0 / Stopwatch.Frequency / secs:0.00} ms/s on the game thread, " +
                $"{renders / secs:0.0} renders/s ({keyChanges} from changes) | game: {(total - rateBytes) / secs / 1048576:0.00} MB/s, GCs gen0 {g0 - rateG0} gen1 {g1 - rateG1} full {g2 - rateG2}, slow frames {slowFrames}", LogLevel.Trace);
            rateBytes = total; rateG0 = g0; rateG1 = g1; rateG2 = g2; slowFrames = 0;
            modBytes = modTicks = 0; renders = keyChanges = 0;
            rateClock.Restart();
        }
    }

    BitmapDrawable NewDrawable(Bitmap bmp)
    {
        var d = new BitmapDrawable(activity.Resources, bmp);
        d.SetFilterBitmap(false);
        return d;
    }
}

/// <summary>Saved to config.json: tab order, hidden tabs and the tab last open.</summary>
public class ModConfig
{
    public List<string> TabOrder { get; set; } = new() { "Today", "Gifts", "People", "Bag", "Craft", "Aim" };
    public List<string> HiddenTabs { get; set; } = new();
    public string LastTab { get; set; } = "Today";
    /// <summary>Write performance numbers to the SMAPI log every 10 s (for troubleshooting stutter).</summary>
    public bool Diagnostics { get; set; } = false;
}
