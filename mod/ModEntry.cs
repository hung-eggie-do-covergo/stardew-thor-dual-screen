using System;
using System.Collections.Concurrent;
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
    int pendingRedraws;
    long drawTicks, readTicks;
    readonly Stopwatch timer = new();

    public override void Entry(IModHelper helper)
    {
        panels = new Panels(Monitor);
        helper.Events.GameLoop.GameLaunched += (_, _) => OpenWindow();
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
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
            }
            catch (Exception ex) { Monitor.Log($"Couldn't open the bottom screen: {ex}", LogLevel.Error); }
        });

        // Hide while the app is paused or the Thor sleeps, so the panel never keeps it awake.
        Microsoft.Xna.Framework.AndroidGameActivity.Paused += (_, _) => window?.Hide();
        Microsoft.Xna.Framework.AndroidGameActivity.Resumed += (_, _) => window?.Show();
    }

    void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
    {
        if (view == null) return;

        while (touches.TryDequeue(out var t))
        {
            panels.Touch(t.action, t.x, t.y);
            // Two renders: readback lags one frame behind the draw.
            pendingRedraws = 2;
        }

        if (pendingRedraws == 0 && !e.IsMultipleOf((uint)panels.RedrawInterval)) return;
        if (pendingRedraws > 0) pendingRedraws--;
        Render();
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
        activity.RunOnUiThread(() =>
        {
            var direct = pixels[f] ??= Java.Nio.ByteBuffer.AllocateDirect(buf.Length);
            Marshal.Copy(buf, 0, Android.Runtime.JNIEnv.GetDirectBufferAddress(direct.Handle), buf.Length);
            direct.Rewind();
            var bmp = bitmaps[f] ??= Bitmap.CreateBitmap(W, H, Bitmap.Config.Argb8888);
            bmp.CopyPixelsFromBuffer(direct);
            view.SetImageDrawable(drawables[f] ??= NewDrawable(bmp));
            view.Invalidate();
        });

        drawTicks += drawn; readTicks += readDone - drawn;
        if (++frames % 300 == 0)
        {
            Monitor.Log($"Bottom screen ms/update: draw {drawTicks / frames / 1e4:0.00}, read {readTicks / frames / 1e4:0.00}", LogLevel.Trace);
            drawTicks = readTicks = frames = 0;
        }
    }

    BitmapDrawable NewDrawable(Bitmap bmp)
    {
        var d = new BitmapDrawable(activity.Resources, bmp);
        d.SetFilterBitmap(false);
        return d;
    }
}
