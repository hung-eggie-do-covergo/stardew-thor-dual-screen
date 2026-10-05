using System;
using System.Collections.Concurrent;
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
/// SpriteBatch, copies the pixels into an Android ImageView, and routes taps back.</summary>
public class ModEntry : Mod
{
    // Half the bottom display (1240x1080); shown at 2x with no filtering so pixel art stays crisp.
    public const int W = 620, H = 540;

    Activity activity;
    Presentation window;
    ImageView view;
    RenderTarget2D target;
    SpriteBatch batch;
    Panels panels;
    readonly byte[] bytes = new byte[W * H * 4];
    readonly Bitmap[] bitmaps = new Bitmap[2];
    readonly ConcurrentQueue<(int x, int y)> taps = new();
    int flip, frames;
    bool redrawNow;
    long costTicks;
    readonly Stopwatch timer = new();

    public override void Entry(IModHelper helper)
    {
        panels = new Panels();
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
                    if (e.Event.Action == MotionEventActions.Up && view.Width > 0)
                        taps.Enqueue(((int)(e.Event.GetX() * W / view.Width), (int)(e.Event.GetY() * H / view.Height)));
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

        while (taps.TryDequeue(out var tap))
        {
            panels.Tap(tap.x, tap.y);
            redrawNow = true;
        }

        if (!redrawNow && !e.IsMultipleOf((uint)panels.RedrawInterval)) return;
        redrawNow = false;
        Render();
    }

    void Render()
    {
        timer.Restart();
        var gd = Game1.graphics.GraphicsDevice;
        target ??= new RenderTarget2D(gd, W, H);
        batch ??= new SpriteBatch(gd);

        var oldTargets = gd.GetRenderTargets();
        var oldViewport = gd.Viewport;
        gd.SetRenderTarget(target);
        gd.Clear(new XColor(0, 0, 0));
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        try { panels.Draw(batch); }
        catch (Exception ex) { Monitor.LogOnce($"Panel draw failed: {ex}", LogLevel.Error); }
        batch.End();
        gd.SetRenderTargets(oldTargets);
        gd.Viewport = oldViewport;

        target.GetData(bytes);
        flip ^= 1;
        var bmp = bitmaps[flip] ??= Bitmap.CreateBitmap(W, H, Bitmap.Config.Argb8888);
        bmp.CopyPixelsFromBuffer(Java.Nio.ByteBuffer.Wrap(bytes));
        activity.RunOnUiThread(() =>
        {
            var d = new BitmapDrawable(activity.Resources, bmp);
            d.SetFilterBitmap(false);
            view.SetImageDrawable(d);
        });

        costTicks += timer.Elapsed.Ticks;
        if (++frames % 120 == 0)
        {
            Monitor.Log($"Bottom screen: {costTicks / frames / 10000.0:0.00} ms per update", LogLevel.Trace);
            costTicks = frames = 0;
        }
    }
}
