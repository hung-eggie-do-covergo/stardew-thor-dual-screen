using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.TerrainFeatures;
using StardewValley.WorldMaps;
using SObject = StardewValley.Object;

namespace DualScreen;

/// <summary>The bottom-screen panels, drawn with the game's own UI sprites on a fixed 620x540 layout.</summary>
public class Panels
{
    // Choices and Shop aren't in the strip: they take over while that kind of menu is open on top.
    enum Tab { Today, Gifts, People, Bag, Craft, Aim, Tabs, Choices, Shop, Bundle, Shipped, LevelUp, Saving }

    static readonly Tab[] Movable = { Tab.Today, Tab.Gifts, Tab.People, Tab.Bag, Tab.Craft, Tab.Aim };
    // Tabs keep a fixed size (about 24x10 mm on the Thor) and the strip scrolls sideways when they don't fit.
    const int TabH = 64, TabW = 140, TabGap = 4, Slot = 48, Cols = 12;
    // Content cards are laid out from y=58: shifted down under the header, or up to the top when a menu
    // takeover hides the header (the tabs can't be used then anyway).
    static int ContentShift => HeaderShown ? TabH + 6 - 58 : 6 - 58;
    static bool HeaderShown => MenuLayout == null;

    // Cursors.png sprites.
    static readonly Rectangle BoxSrc = new(384, 373, 18, 18), Dice = new(381, 361, 10, 10), Coin = new(193, 373, 9, 10),
        HeartFull = new(211, 428, 7, 6), HeartEmpty = new(218, 428, 7, 6), Gift = new(229, 410, 14, 14),
        CheckOff = new(227, 425, 9, 9), CheckOn = new(236, 425, 9, 9),
        Calendar = new(48, 384, 16, 16), Backpack = new(4, 371, 8, 10);

    static readonly Color Ink = new(86, 22, 12);
    static readonly Color Paper = new(255, 214, 147);
    static readonly Color Faint = new(150, 90, 60);

    Tab tab = Tab.Today;
    Texture2D logo;
    readonly IMonitor monitor;
    readonly IModHelper helper;
    readonly ModConfig config;

    public Panels(IModHelper helper, IMonitor monitor)
    {
        this.helper = helper;
        this.monitor = monitor;
        config = helper.ReadConfig<ModConfig>();
        // Drop names this version doesn't know and add any new tabs at the end.
        config.TabOrder = config.TabOrder.Where(n => Enum.TryParse<Tab>(n, out var t) && Movable.Contains(t)).Distinct().ToList();
        config.TabOrder.AddRange(Movable.Select(t => t.ToString()).Except(config.TabOrder));
        if (Enum.TryParse<Tab>(config.LastTab, out var last) && Visible.Contains(last)) tab = last;
    }

    /// <summary>Tabs in the strip, in the player's order; the Tabs settings tab is always last.</summary>
    List<Tab> Visible => config.TabOrder.Where(n => !config.HiddenTabs.Contains(n)).Select(Enum.Parse<Tab>).Append(Tab.Tabs).ToList();

    void Open(Tab t)
    {
        gridScroll = 0;
        tab = t;
        config.LastTab = t.ToString();
        helper.WriteConfig(config);
    }

    // "Who loves this" scans every villager, so only redo it when the held item changes.
    string lovedFor;
    List<NPC> lovedBy = new(), likedBy = new();
    List<string> bundles = new();

    public int RedrawInterval => TitleAnimating ? 2 : Game1.gameMode == Game1.loadingMode || Showing == Tab.Saving ? 6 : Showing switch { Tab.Aim => 2, Tab.Bag => dragging ? 2 : 15, Tab.Craft or Tab.Shop or Tab.People => listTouch ? 2 : 30, _ => 30 };

    /// <summary>Menu whose inventory the Bag panel drives, e.g. a chest or a shop.</summary>
    static InventoryMenu OpenInventory => Game1.activeClickableMenu switch
    {
        MenuWithInventory m => m.inventory,
        ShopMenu s => s.inventory,
        _ => null,
    };

    // Panels stay up behind menus; only the title screen and cutscenes show the logo.
    static bool Idle => !Context.IsWorldReady || Game1.eventUp;

    /// <summary>The layout a top menu takes over the bottom screen with, if any.</summary>
    static Tab? MenuLayout => Game1.activeClickableMenu switch
    {
        DialogueBox { isQuestion: true } d when d.responses.Length > 0 => Tab.Choices,
        ShopMenu => Tab.Shop,
        JunimoNoteMenu => Tab.Bundle,
        ShippingMenu => Tab.Shipped,
        LevelUpMenu => Tab.LevelUp,
        SaveGameMenu => Tab.Saving,
        MenuWithInventory => Tab.Bag,
        _ => null,
    };

    Tab? Showing => Idle ? null : MenuLayout ?? tab;

    public string ShowingName => Showing?.ToString() ?? (Context.IsWorldReady ? "logo" : "title");

    // Header strip scroll, in pixels; swiping the header moves it.
    int tabScroll, scrollStartX, scrollStartValue;
    bool scrollingTabs, headerTouch;

    int MaxTabScroll => Math.Max(0, TabGap + Visible.Count * (TabW + TabGap) - ModEntry.W);

    // Bag drag-and-drop: the slot the finger went down on, and where it is now.
    int dragFrom = -1;
    Point dragAt;
    bool dragging;

    public void Touch(Android.Views.MotionEventActions action, int x, int y)
    {
        switch (action)
        {
            case Android.Views.MotionEventActions.Down when HeaderShown && y < TabH:
                scrollStartX = x; scrollStartValue = tabScroll;
                headerTouch = true; scrollingTabs = false; dragFrom = -1; dragging = false;
                break;
            case Android.Views.MotionEventActions.Move when headerTouch:
                scrollingTabs |= Math.Abs(x - scrollStartX) > 12;
                if (scrollingTabs) tabScroll = Math.Clamp(scrollStartValue - (x - scrollStartX), 0, MaxTabScroll);
                break;
            case Android.Views.MotionEventActions.Down when Showing is Tab.Craft or Tab.Shop or Tab.People && ListView.Contains(x, y - ContentShift):
                listStartY = y; listStartValue = gridScroll;
                listTouch = true; scrollingList = headerTouch = scrollingTabs = false;
                break;
            case Android.Views.MotionEventActions.Move when listTouch:
                scrollingList |= Math.Abs(y - listStartY) > 12;
                if (scrollingList) gridScroll = Math.Clamp(listStartValue - (y - listStartY), 0, MaxGridScroll);
                break;
            case Android.Views.MotionEventActions.Down:
                headerTouch = scrollingTabs = listTouch = scrollingList = false;
                pressAt = new Point(x, y); pressTick = Game1.ticks; pressDown = true; longPressed = false;
                dragFrom = Showing == Tab.Bag && OpenInventory == null ? BagSlotAt(x, y - ContentShift) : -1;
                if (dragFrom >= 0 && Game1.player.Items[dragFrom] == null) dragFrom = -1;
                dragAt = new Point(x, y);
                dragging = false;
                break;
            case Android.Views.MotionEventActions.Move when pressDown && dragFrom < 0:
                if (Math.Abs(x - pressAt.X) + Math.Abs(y - pressAt.Y) > 12) pressDown = false;
                break;
            case Android.Views.MotionEventActions.Move when dragFrom >= 0:
                // A few pixels of wobble still counts as a tap.
                dragging |= Math.Abs(x - dragAt.X) + Math.Abs(y - dragAt.Y) > 12;
                if (dragging) dragAt = new Point(x, y);
                break;
            case Android.Views.MotionEventActions.Up:
                if (dragging) DropItem(dragFrom, x, y - ContentShift);
                else if (!scrollingTabs && !scrollingList && !longPressed) Tap(x, y);
                pressDown = longPressed = false;
                headerTouch = scrollingTabs = listTouch = scrollingList = false;
                dragFrom = -1; dragging = false;
                break;
            case Android.Views.MotionEventActions.Cancel:
                dragFrom = -1; dragging = false;
                break;
        }
    }

    void Tap(int x, int y)
    {
        if (!Context.IsWorldReady && Game1.activeClickableMenu is TitleMenu tm) { TapTitle(tm, x, y); return; }
        if (Idle) return;
        if (numpadMax > 0) { TapNumpad(x, y - ContentShift); return; }
        if (HeaderShown && y < TabH)
        {
            int i = (x + tabScroll - TabGap) / (TabW + TabGap);
            var visible = Visible;
            if (MenuLayout == null && i >= 0 && i < visible.Count) Open(visible[i]);
            return;
        }
        if (Showing == Tab.Bag) TapBag(x, y - ContentShift);
        else if (Showing == Tab.Craft) TapCraft(x, y - ContentShift);
        else if (Showing == Tab.People) TapPeople(x, y - ContentShift);
        else if (Showing == Tab.Tabs) TapTabSettings(x, y - ContentShift);
        else if (Showing == Tab.Choices) TapChoices(x, y - ContentShift);
        else if (Showing == Tab.Shop) TapShop(x, y - ContentShift);
        else if (Showing == Tab.Bundle) TapBundle((JunimoNoteMenu)Game1.activeClickableMenu, x, y - ContentShift);
        else if (Showing == Tab.Shipped) TapShipped((ShippingMenu)Game1.activeClickableMenu, x, y - ContentShift);
        else if (Showing == Tab.LevelUp) TapLevelUp((LevelUpMenu)Game1.activeClickableMenu, x, y - ContentShift);
        else if (Showing == Tab.Aim) TapAim(x, y);
    }

    public void Draw(SpriteBatch b)
    {
        if (Showing is not Tab shown)
        {
            if (!Context.IsWorldReady && Game1.activeClickableMenu is TitleMenu tm) DrawTitle(b, tm);
            else DrawLogo(b);
            return;
        }

        b.Draw(Game1.staminaRect, new Rectangle(0, 0, ModEntry.W, ModEntry.H), Paper);
        if (shown == Tab.Aim) DrawAim(b);
        else
        {
            b.End();
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, Matrix.CreateTranslation(0, ContentShift, 0));
            if (shown == Tab.Today) DrawToday(b);
            else if (shown == Tab.Gifts) DrawNearby(b);
            else if (shown == Tab.People) DrawPeople(b);
            else if (shown == Tab.Craft) DrawCraft(b);
            else if (shown == Tab.Tabs) DrawTabSettings(b);
            else if (shown == Tab.Choices) DrawChoices(b);
            else if (shown == Tab.Shop) DrawShop(b);
            else if (shown == Tab.Bundle) DrawBundle(b, (JunimoNoteMenu)Game1.activeClickableMenu);
            else if (shown == Tab.Shipped) DrawShipped(b, (ShippingMenu)Game1.activeClickableMenu);
            else if (shown == Tab.LevelUp) DrawLevelUp(b, (LevelUpMenu)Game1.activeClickableMenu);
            // Drawn inside the shifted content layer, so cancel the shift to sit in the same corner.
            else if (shown == Tab.Saving)
                DrawLoadingStrip(b, Game1.content.LoadString("Strings\\StringsFromCSFiles:SaveGameMenu.cs.11378").TrimEnd('.'), ContentShift);
            else
            {
                DrawBag(b);
                // Last, so the dragged item floats over the item card; a bit larger to show past the fingertip.
                if (dragging && Game1.player.Items[dragFrom] is Item held)
                    Item(b, held, dragAt.X - 32, dragAt.Y - ContentShift - 32, 64);
            }
            if (numpadMax > 0) DrawNumpad(b);
            b.End();
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        }
        if (HeaderShown) DrawHeader(b, shown);
    }

    // ---------- shared ----------

    /// <summary>The game's load-screen strip: its own font on a scroll, dots cycling every third of a second,
    /// bottom-left like the top screen.</summary>
    static void DrawLoadingStrip(SpriteBatch b, string label, int yShift = 0)
    {
        string dots = "".PadRight((int)Math.Ceiling(Game1.currentGameTime.TotalGameTime.TotalMilliseconds % 999.0 / 333.0), '.');
        string widest = label + "... ";
        StardewValley.BellsAndWhistles.SpriteText.drawString(b, label + dots, 32, ModEntry.H - 96 - yShift, 999999,
            StardewValley.BellsAndWhistles.SpriteText.getWidthOfString(widest), 64, 1f, 0.88f, false, 0, widest);
    }

    void DrawLogo(SpriteBatch b)
    {
        if (Game1.gameMode == Game1.loadingMode)
        {
            b.Draw(Game1.staminaRect, new Rectangle(0, 0, ModEntry.W, ModEntry.H), Game1.bgColor);
            DrawLoadingStrip(b, Game1.content.LoadString("Strings\\StringsFromCSFiles:Game1.cs.3688"));
            return;
        }
        logo ??= Game1.content.Load<Texture2D>("Minigames\\TitleButtons");
        var src = new Rectangle(0, 0, 398, 187);
        b.Draw(logo, new Vector2((ModEntry.W - src.Width) / 2, (ModEntry.H - src.Height) / 2), src, Color.White * 0.6f);
    }

    /// <summary>Four labelled tab buttons across the top; the open one is lit and drops onto the page.</summary>
    void DrawHeader(SpriteBatch b, Tab shown)
    {
        b.Draw(Game1.staminaRect, new Rectangle(0, 0, ModEntry.W, TabH), new Color(214, 147, 86));
        b.Draw(Game1.staminaRect, new Rectangle(0, TabH - 2, ModEntry.W, 2), Ink);
        var visible = Visible;
        for (int i = 0; i < visible.Count; i++)
        {
            bool on = visible[i] == shown;
            int x = TabGap + i * (TabW + TabGap) - tabScroll, y = on ? 6 : 2;
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, BoxSrc, x, y, TabW, TabH - 6, on ? Color.White : new Color(200, 160, 120), 2f, false);
            TabIcon(b, visible[i], x + 12, y + 12);
            Text(b, visible[i].ToString(), new Vector2(x + 56, y + 16), on ? Ink : Faint);
        }

        // Edge arrows over a fade, only on sides that have more tabs to scroll to.
        var bg = new Color(214, 147, 86);
        for (int side = 0; side < 2; side++)
        {
            if (side == 0 ? tabScroll <= 0 : tabScroll >= MaxTabScroll) continue;
            for (int k = 0; k < 24; k++)
            {
                int fx = side == 0 ? k : ModEntry.W - 1 - k;
                b.Draw(Game1.staminaRect, new Rectangle(fx, 0, 1, TabH - 2), bg * (1f - k / 24f));
            }
            Icon(b, Game1.mouseCursors, side == 0 ? ArrowLeft : ArrowRight, side == 0 ? 2 : ModEntry.W - 26, 22, 2);
        }
    }

    /// <summary>A 36x36 icon that says what the tab is for.</summary>
    static Item hoe, workbench;

    static void TabIcon(SpriteBatch b, Tab t, int x, int y)
    {
        switch (t)
        {
            case Tab.Today: WeatherIcon(b, Game1.weatherIcon, x, y + 6, 3); break;
            case Tab.Gifts: Icon(b, Game1.mouseCursors, Gift, x - 2, y - 2, 3); break;
            case Tab.Bag: Icon(b, Game1.mouseCursors, Backpack, x + 6, y - 2, 3.5f); break;
            case Tab.Craft: Item(b, workbench ??= ItemRegistry.Create("(BC)208"), x - 4, y - 6, 44); break;
            case Tab.Aim: Item(b, hoe ??= ItemRegistry.Create("(T)Hoe"), x - 4, y - 4, 44); break;
            // The game menu's options tab sprite.
            case Tab.Tabs: Icon(b, Game1.mouseCursors, new Rectangle(96, 368, 16, 16), x - 2, y - 2, 2.5f); break;
            // The game menu's social tab sprite.
            case Tab.People: Icon(b, Game1.mouseCursors, new Rectangle(32, 368, 16, 16), x - 2, y - 2, 2.5f); break;
        }
    }

    static void Icon(SpriteBatch b, Texture2D tex, Rectangle src, int x, int y, float scale) =>
        b.Draw(tex, new Vector2(x, y), src, Color.White, 0, Vector2.Zero, scale, SpriteEffects.None, 0);

    static void WeatherIcon(SpriteBatch b, int icon, int x, int y, float scale)
    {
        if (icon == 999) Icon(b, Game1.mouseCursors_1_6, new Rectangle(243, 293, 12, 8), x, y, scale);
        else Icon(b, Game1.mouseCursors, new Rectangle(317 + 12 * icon, 421, 12, 8), x, y, scale);
    }

    /// <summary>The game's tooltip box, used as a card frame.</summary>
    static void Card(SpriteBatch b, int x, int y, int w, int h) =>
        IClickableMenu.drawTextureBox(b, Game1.mouseCursors, BoxSrc, x, y, w, h, Color.White, 2f, false);

    static void SlotFrame(SpriteBatch b, int x, int y, int size) =>
        b.Draw(Game1.menuTexture, new Rectangle(x, y, size, size), new Rectangle(128, 128, 64, 64), Color.White);

    static void Text(SpriteBatch b, string s, Vector2 at, Color? c = null) =>
        b.DrawString(Game1.smallFont, s, at, c ?? Ink);

    static float Line => Game1.smallFont.LineSpacing;

    /// <summary>Draws an item so its icon fills a size-by-size box at (x, y).</summary>
    static void Item(SpriteBatch b, Item item, int x, int y, int size = 48, float alpha = 1f)
    {
        float scale = size / 64f;
        // HideButShowQuality keeps quality stars and the watering-can gauge; stack counts are drawn separately.
        // Small icons skip both, since the game draws them at full slot size.
        var extras = size >= 44 ? StackDrawType.HideButShowQuality : StackDrawType.Hide;
        item.drawInMenu(b, new Vector2(x - 32 * (1 - scale), y - 32 * (1 - scale)), scale, alpha, 0.9f, extras, Color.White, false);
    }

    static void Count(SpriteBatch b, int n, int right, int bottom)
    {
        if (n > 1) Utility.drawTinyDigits(n, b, new Vector2(right - Utility.getWidthOfTinyDigitString(n, 2f) - 2, bottom - 14), 2f, 1f, Color.White);
    }

    /// <summary>Head-and-shoulders crop of a villager's sprite, 32x32.</summary>
    static void Head(SpriteBatch b, NPC npc, int x, int y)
    {
        var src = npc.getMugShotSourceRect();
        src.Height = 16;
        Icon(b, npc.Sprite.Texture, src, x, y, 2);
    }

    static void Portrait(SpriteBatch b, NPC npc, int x, int y)
    {
        SlotFrame(b, x, y, 72);
        if (npc.Portrait != null) Icon(b, npc.Portrait, new Rectangle(0, 0, 64, 64), x + 4, y + 4, 1);
        else Head(b, npc, x + 20, y + 20);
    }

    static void Hearts(SpriteBatch b, int hearts, int x, int y)
    {
        for (int i = 0; i < 10; i++) Icon(b, Game1.mouseCursors, i < hearts ? HeartFull : HeartEmpty, x + i * 16, y, 2);
    }

    /// <summary>Word-wraps to a fixed width, cutting off after <paramref name="maxLines"/>.</summary>
    static void Wrapped(SpriteBatch b, string s, int x, ref float y, int width, Color c, int maxLines)
    {
        var lines = Game1.parseText(s, Game1.smallFont, width).Split('\n');
        for (int i = 0; i < Math.Min(lines.Length, maxLines); i++)
        {
            string line = i == maxLines - 1 && lines.Length > maxLines ? lines[i].TrimEnd(',', ' ') + "..." : lines[i];
            Text(b, line, new Vector2(x, y), c);
            y += Line;
        }
    }

    // ---------- Today ----------

    void DrawToday(SpriteBatch b)
    {
        // Row 1: luck, tomorrow's weather, crops.
        Card(b, 8, 58, 196, 76);
        Icon(b, Game1.mouseCursors, Dice, 22, 74, 4);
        Text(b, "Luck", new Vector2(70, 68), Faint);
        Text(b, Luck(Game1.player.DailyLuck), new Vector2(70, 94), LuckColor(Game1.player.DailyLuck));

        Card(b, 212, 58, 196, 76);
        WeatherIcon(b, TomorrowIcon(Game1.weatherForTomorrow), 224, 80, 3);
        Text(b, "Tomorrow", new Vector2(270, 68), Faint);
        Text(b, Weather(Game1.weatherForTomorrow), new Vector2(270, 94));

        Card(b, 416, 58, 196, 76);
        var (ready, readyItem, dry) = Crops();
        Item(b, readyItem ?? (parsnip ??= ItemRegistry.Create("(O)24")), 428, 68, 32, readyItem == null ? 0.35f : 1f);
        Text(b, ready > 0 ? $"{ready} ready" : "None ready", new Vector2(466, 68), ready > 0 ? Color.DarkGreen : Faint);
        Item(b, waterCan ??= ItemRegistry.Create("(T)WateringCan"), 428, 98, 32);
        Text(b, dry > 0 ? $"{dry} dry" : "All watered", new Vector2(466, 98), dry > 0 ? Color.DarkRed : Faint);

        // Row 2: what's on in the next few days.
        Card(b, 8, 142, 604, 132);
        Text(b, "Coming up", new Vector2(22, 150), Faint);
        float y = 176;
        foreach (var (icon, line) in Events().Take(3))
        {
            icon(b, 22, (int)y);
            Text(b, line, new Vector2(62, y + 2));
            y += 32;
        }

        // Row 3: birthdays this week, one fixed cell each.
        Card(b, 8, 282, 604, 250 - ContentShift);
        Text(b, "Birthdays this week", new Vector2(22, 290), Faint);
        var soon = Birthdays().Take(4).ToList();
        if (soon.Count == 0) Text(b, "None", new Vector2(22, 320));
        for (int i = 0; i < soon.Count; i++)
        {
            var (npc, inDays) = soon[i];
            int cx = 22 + i * 148;
            Portrait(b, npc, cx, 320);
            Text(b, npc.displayName, new Vector2(cx, 398));
            bool gifted = Game1.player.friendshipData.TryGetValue(npc.Name, out var f) && f.GiftsToday > 0;
            string when = inDays switch { 0 => "Today!", 1 => "Tomorrow", _ => $"In {inDays} days" };
            Text(b, when, new Vector2(cx, 422), inDays == 0 && !gifted ? Color.DarkRed : Faint);
            if (inDays == 0) Icon(b, Game1.mouseCursors, gifted ? CheckOn : Gift, cx, 450, 2);
        }
    }

    Item waterCan, tv, parsnip;

    static string Luck(double luck) => luck switch
    {
        > 0.07 => "Very good",
        > 0.02 => "Good",
        >= -0.02 => "Neutral",
        >= -0.07 => "Bad",
        _ => "Very bad",
    };

    static Color LuckColor(double luck) => luck > 0.02 ? Color.DarkGreen : luck < -0.02 ? Color.DarkRed : Ink;

    static string Weather(string w) => w switch
    {
        "Rain" => "Rain",
        "Storm" => "Storm",
        "Snow" => "Snow",
        "Wind" => "Windy",
        "GreenRain" => "Green rain",
        "Festival" => "Festival",
        "Wedding" => "Wedding",
        _ => "Sunny",
    };

    // Same icon indices Game1.updateWeatherIcon uses for today.
    static int TomorrowIcon(string w) => w switch
    {
        "Rain" => 4,
        "Storm" => 5,
        "Snow" => 7,
        "Wind" => Game1.IsFall ? 6 : Game1.IsWinter ? 7 : 3,
        "Festival" => 1,
        "Wedding" => 0,
        "GreenRain" => 999,
        _ => 2,
    };

    static (int ready, Item readyItem, int dry) Crops()
    {
        int ready = 0, dry = 0;
        var harvests = new Dictionary<string, int>();
        foreach (var loc in new[] { Game1.getFarm(), Game1.getLocationFromName("Greenhouse") })
        {
            if (loc == null) continue;
            bool rain = loc.IsOutdoors && loc.IsRainingHere();
            var dirts = loc.terrainFeatures.Values.OfType<HoeDirt>()
                .Concat(loc.objects.Values.OfType<StardewValley.Objects.IndoorPot>().Select(p => p.hoeDirt.Value));
            foreach (var d in dirts)
            {
                if (d?.crop == null || d.crop.dead.Value) continue;
                if (d.readyForHarvest())
                {
                    ready++;
                    string id = d.crop.indexOfHarvest.Value;
                    if (id != null) harvests[id] = harvests.GetValueOrDefault(id) + 1;
                }
                else if (!rain && d.needsWatering() && !d.isWatered()) dry++;
            }
        }
        // Show the crop you have most of.
        var top = harvests.OrderByDescending(h => h.Value).Select(h => h.Key).FirstOrDefault();
        return (ready, top == null ? null : ItemRegistry.Create(top, allowNull: true), dry);
    }

    IEnumerable<(Action<SpriteBatch, int, int> icon, string line)> Events()
    {
        var festivals = DataLoader.Festivals_FestivalDates(Game1.temporaryContent);
        for (int i = 0; i <= 2; i++)
        {
            int day = Game1.dayOfMonth + i;
            if (day > 28) break;
            string when = i == 0 ? "Today" : i == 1 ? "Tomorrow" : "In 2 days";
            if (festivals.TryGetValue($"{Game1.currentSeason}{day}", out var name))
                yield return ((b, x, y) => Icon(b, Game1.mouseCursors, Calendar, x, y - 2, 2), $"{when}: {name}");
            if (day % 7 is 5 or 0)
                yield return ((b, x, y) => Icon(b, Game1.mouseCursors, Coin, x + 4, y, 3), $"{when}: Traveling cart");
            if (i == 0 && day % 7 == 0)
                yield return ((b, x, y) => Item(b, tv ??= ItemRegistry.Create("(F)1466"), x, y - 2, 32), "Today: new Queen of Sauce recipe");
        }
    }

    static IEnumerable<(NPC npc, int inDays)> Birthdays()
    {
        var found = new List<(NPC, int)>();
        Utility.ForEachVillager(npc =>
        {
            if (npc.CanSocialize && npc.Birthday_Season == Game1.currentSeason)
            {
                int inDays = npc.Birthday_Day - Game1.dayOfMonth;
                if (inDays is >= 0 and <= 7) found.Add((npc, inDays));
            }
            return true;
        });
        return found.OrderBy(p => p.Item2);
    }

    // ---------- Nearby ----------

    void DrawNearby(SpriteBatch b)
    {
        // Left card: the held item and who wants it.
        Card(b, 8, 58, 296, 474 - ContentShift);
        var held = Game1.player.ActiveObject;
        if (held == null || held.bigCraftable.Value)
            Text(b, "Hold an item to see\nwho loves it.", new Vector2(22, 70), Faint);
        else
        {
            RefreshHeld(held);
            SlotFrame(b, 22, 70, 64);
            Item(b, held, 22, 70, 64);
            float ny = 72;
            Wrapped(b, held.DisplayName, 96, ref ny, 196, Ink, 2);
            Icon(b, Game1.mouseCursors, Coin, 96, 122, 2);
            Text(b, held.sellToStorePrice().ToString(), new Vector2(118, 118));

            HeadsRow(b, "Loves it", HeartFull, lovedBy, 150);
            HeadsRow(b, "Likes it", HeartEmpty, likedBy, 250);
            if (bundles.Count > 0)
            {
                Text(b, "Bundle", new Vector2(22, 350), Faint);
                float by = 374;
                Wrapped(b, string.Join(", ", bundles), 22, ref by, 268, Color.DarkGreen, 5);
            }
        }

        // Right card: the nearest villager.
        Card(b, 316, 58, 296, 474 - ContentShift);
        var npc = NearestVillager(8);
        if (npc == null) { Text(b, "Nobody nearby.", new Vector2(330, 70), Faint); return; }

        DrawVillager(b, npc, held, 330, 70);
    }

    /// <summary>Portrait, hearts, this week's gifts, the held item's reaction and loved items, from (ox, oy).</summary>
    void DrawVillager(SpriteBatch b, NPC npc, SObject held, int ox, int oy)
    {
        Portrait(b, npc, ox, oy);
        Text(b, npc.displayName, new Vector2(ox + 82, oy + 2));
        Game1.player.friendshipData.TryGetValue(npc.Name, out var f);
        Hearts(b, (f?.Points ?? 0) / NPC.friendshipPointsPerHeartLevel, ox + 82, oy + 34);
        if (f == null) Text(b, "Not met yet", new Vector2(ox + 82, oy + 50), Faint);
        else
        {
            // Same gift/week markers as the social page.
            Icon(b, Game1.mouseCursors, Gift, ox + 82, oy + 50, 2);
            Icon(b, Game1.mouseCursors, f.GiftsThisWeek >= 1 ? CheckOn : CheckOff, ox + 116, oy + 54, 2);
            Icon(b, Game1.mouseCursors, f.GiftsThisWeek >= 2 ? CheckOn : CheckOff, ox + 138, oy + 54, 2);
            if (npc.isBirthday()) Text(b, "Birthday!", new Vector2(ox + 166, oy + 52), Color.DarkRed);
        }

        if (held != null && npc.CanReceiveGifts())
        {
            int t = npc.getGiftTasteForThisItem(held);
            Icon(b, Game1.mouseCursors, t == NPC.gift_taste_love || t == NPC.gift_taste_like ? HeartFull : HeartEmpty, ox + 0, oy + 90, 2);
            Text(b, Taste(t), new Vector2(ox + 20, oy + 82), t == NPC.gift_taste_love ? Color.DarkGreen : t >= NPC.gift_taste_dislike && t != 8 ? Color.DarkRed : Ink);
        }

        Text(b, "Loves", new Vector2(ox + 0, oy + 114), Faint);
        int i = 0;
        foreach (var item in Loves(npc).Take(24))
        {
            int x = ox + i % 6 * 46, y = oy + 140 + i / 6 * 46;
            SlotFrame(b, x, y, 44);
            Item(b, item, x + 2, y + 2, 40);
            i++;
        }
    }

    /// <summary>A labelled row of villager heads, fixed at 8 per row and two rows.</summary>
    static void HeadsRow(SpriteBatch b, string label, Rectangle icon, List<NPC> npcs, int y)
    {
        Icon(b, Game1.mouseCursors, icon, 22, y + 6, 2);
        Text(b, label, new Vector2(42, y), Faint);
        if (npcs.Count == 0) { Text(b, "Nobody", new Vector2(22, y + 30), Faint); return; }
        for (int i = 0; i < Math.Min(npcs.Count, 16); i++)
            Head(b, npcs[i], 22 + i % 8 * 34, y + 26 + i / 8 * 34);
        if (npcs.Count > 16) Text(b, $"+{npcs.Count - 16}", new Vector2(240, y), Faint);
    }

    static string Taste(int t) => t switch
    {
        NPC.gift_taste_love => "Loves your item",
        NPC.gift_taste_like => "Likes your item",
        NPC.gift_taste_dislike => "Dislikes your item",
        NPC.gift_taste_hate => "Hates your item",
        _ => "Neutral on your item",
    };

    static NPC NearestVillager(int tiles)
    {
        var me = Game1.player.Tile;
        return Game1.currentLocation?.characters
            .Where(n => n.IsVillager && n.CanSocialize && Vector2.Distance(n.Tile, me) <= tiles)
            .OrderBy(n => Vector2.Distance(n.Tile, me))
            .FirstOrDefault();
    }

    static IEnumerable<Item> Loves(NPC npc)
    {
        if (!Game1.NPCGiftTastes.TryGetValue(npc.Name, out var data)) yield break;
        var fields = data.Split('/');
        if (fields.Length < 2) yield break;
        // Negative ids are categories; only show real items.
        foreach (var id in ArgUtility.SplitBySpace(fields[1]).Where(id => !id.StartsWith('-')))
        {
            var item = ItemRegistry.Create(id, allowNull: true);
            if (item != null) yield return item;
        }
    }

    void RefreshHeld(SObject held)
    {
        string key = held.QualifiedItemId + held.Quality;
        if (key == lovedFor) return;
        lovedFor = key;
        lovedBy = new(); likedBy = new();
        Utility.ForEachVillager(npc =>
        {
            // Skip people you haven't met yet: showing their faces would spoil them.
            if (npc.CanReceiveGifts() && Met(npc))
            {
                int t = npc.getGiftTasteForThisItem(held);
                if (t == NPC.gift_taste_love) lovedBy.Add(npc);
                else if (t == NPC.gift_taste_like) likedBy.Add(npc);
            }
            return true;
        });
        bundles = BundlesNeeding(held);
    }

    static List<string> BundlesNeeding(SObject o)
    {
        var names = new List<string>();
        var cc = Game1.getLocationFromName("CommunityCenter") as CommunityCenter;
        if (cc == null || Game1.player.mailReceived.Contains("JojaMember") || cc.areAllAreasComplete()) return names;
        var done = cc.bundlesDict();
        foreach (var (key, value) in Game1.netWorldState.Value.BundleData)
        {
            int index = Convert.ToInt32(key.Split('/')[1]);
            var parts = value.Split('/');
            var ingredients = ArgUtility.SplitBySpace(parts[2]);
            for (int i = 0; i + 2 < ingredients.Length; i += 3)
            {
                if (done.TryGetValue(index, out var slots) && i / 3 < slots.Length && slots[i / 3]) continue;
                string id = ingredients[i];
                bool match = id.StartsWith('-')
                    ? id == o.Category.ToString()
                    : ItemRegistry.QualifyItemId(id) == o.QualifiedItemId;
                if (match && o.Quality >= Convert.ToInt32(ingredients[i + 2]))
                {
                    names.Add(parts.Length > 6 && parts[6].Length > 0 ? parts[6] : parts[0]);
                    break;
                }
            }
        }
        return names;
    }

    // ---------- People ----------

    // Left: a scrolling list of names. Right: the picked villager on the game's own world map.
    const int PeopleRowH = 48;
    static readonly Rectangle PeopleView = new(14, 66, 224, 454);
    // Map | Gifts switch at the top of the right card; both views stay on this tab.
    static readonly Rectangle ShowMap = new(262, 66, 168, 52), ShowGifts = new(436, 66, 168, 52);
    bool personGifts;
    const float MapScale = 0.25f; // world-map coordinates are 4x; 0.25 draws the source art pixel for pixel
    static readonly Point MapOrigin = new(284, 128);
    NPC personPick;

    static bool Met(NPC n) => n.CanSocialize && Game1.player.friendshipData.ContainsKey(n.Name);

    /// <summary>Villagers you've met, where you are first, then by place, then by name.</summary>
    static List<NPC> People()
    {
        var list = new List<NPC>();
        // Only people you've met, so the list doesn't spoil who's out there.
        Utility.ForEachVillager(n => { if (Met(n) && n.currentLocation != null) list.Add(n); return true; });
        var here = Game1.currentLocation;
        return list.OrderBy(n => n.currentLocation == here ? 0 : 1)
            .ThenBy(n => n.currentLocation.DisplayName)
            .ThenBy(n => n.displayName)
            .ToList();
    }

    /// <summary>Location name for people; rooms with only an internal name ("HarveyRoom") get spaces.</summary>
    static string PlaceName(GameLocation loc) =>
        System.Text.RegularExpressions.Regex.Replace(loc.DisplayName, "(?<=[a-z])(?=[A-Z])", " ");

    Rectangle PersonRect(int i) => new(PeopleView.X, PeopleView.Y + i * PeopleRowH - gridScroll, PeopleView.Width, PeopleRowH - 4);

    void DrawPeople(SpriteBatch b)
    {
        var people = People();
        if (personPick != null && !people.Contains(personPick)) personPick = null;

        Card(b, 8, 58, 236, 470);
        DrawScrolled(b, PeopleView, () =>
        {
            for (int i = 0; i < people.Count; i++)
            {
                var r = PersonRect(i);
                if (r.Bottom < PeopleView.Y || r.Y > PeopleView.Bottom) continue;
                var npc = people[i];
                if (npc == personPick) b.Draw(Game1.staminaRect, r, Color.White * 0.5f);
                Head(b, npc, r.X + 4, r.Y + 6);
                Text(b, npc.displayName, new Vector2(r.X + 44, r.Y + 10), npc.currentLocation == Game1.currentLocation ? Color.DarkGreen : Ink);
                if (npc.isBirthday()) Icon(b, Game1.mouseCursors, Gift, r.Right - 32, r.Y + 8, 2);
            }
        });

        Card(b, 252, 58, 360, 470);
        if (personPick == null) { Text(b, "Tap a name to find them.", new Vector2(270, 74), Faint); return; }
        foreach (var (r, label, on, icon) in new[] { (ShowMap, "Map", !personGifts, false), (ShowGifts, "Gifts", personGifts, true) })
        {
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, BoxSrc, r.X, r.Y, r.Width, r.Height, on ? Color.White : new Color(200, 160, 120), 2f, false);
            if (icon) Icon(b, Game1.mouseCursors, Gift, r.X + 16, r.Y + 13, 2);
            else Icon(b, Game1.mouseCursors, new Rectangle(48, 368, 16, 16), r.X + 12, r.Y + 10, 2);
            Text(b, label, new Vector2(r.X + 56, r.Y + 14), on ? Ink : Faint);
        }

        if (personGifts) { DrawVillager(b, personPick, Game1.player.ActiveObject, 268, 132); return; }

        var pos = WorldMapManager.GetPositionData(personPick.currentLocation, personPick.TilePoint);
        if (pos is MapAreaPositionWithContext p) DrawMiniMap(b, p);
        else Text(b, "Not on the map right now.", new Vector2(270, 200), Faint);

        Portrait(b, personPick, 268, 318);
        Text(b, personPick.displayName, new Vector2(352, 324));
        float y = 352;
        Wrapped(b, PlaceName(personPick.currentLocation), 352, ref y, 248, personPick.currentLocation == Game1.currentLocation ? Color.DarkGreen : Faint, 2);
        if (personPick.isBirthday()) Text(b, "Birthday today!", new Vector2(352, 404), Color.DarkRed);
    }

    /// <summary>The game's world map for that region, with the villager's head pinned and you marked.</summary>
    void DrawMiniMap(SpriteBatch b, MapAreaPositionWithContext pos)
    {
        var region = pos.Data.Region;
        Vector2 ToPanel(Vector2 mapPixel) => new(MapOrigin.X + mapPixel.X * MapScale, MapOrigin.Y + mapPixel.Y * MapScale);
        void DrawTex(MapAreaTexture t)
        {
            var a = t.MapPixelArea;
            var at = ToPanel(new Vector2(a.X, a.Y));
            b.Draw(t.Texture, new Rectangle((int)at.X, (int)at.Y, (int)(a.Width * MapScale), (int)(a.Height * MapScale)), t.SourceRect, Color.White);
        }
        if (region.GetBaseTexture() is MapAreaTexture baseTex) DrawTex(baseTex);
        foreach (var area in region.GetAreas())
            foreach (var t in area.GetTextures()) DrawTex(t);

        // You: a small red square, only if you're in the same region.
        var me = WorldMapManager.GetPositionData(Game1.currentLocation, Game1.player.TilePoint);
        if (me is MapAreaPositionWithContext m && m.Data.Region.Id == region.Id)
        {
            var at = ToPanel(m.GetMapPixelPosition());
            b.Draw(Game1.staminaRect, new Rectangle((int)at.X - 4, (int)at.Y - 4, 8, 8), Color.Red);
        }
        // Them: their head, centred on the spot, with a dark outline so it reads on any terrain.
        var pin = ToPanel(pos.GetMapPixelPosition());
        b.Draw(Game1.staminaRect, new Rectangle((int)pin.X - 18, (int)pin.Y - 18, 36, 36), Ink * 0.8f);
        Head(b, personPick, (int)pin.X - 16, (int)pin.Y - 16);
    }

    void TapPeople(int x, int y)
    {
        if (personPick != null && ShowMap.Contains(x, y)) { personGifts = false; return; }
        if (personPick != null && ShowGifts.Contains(x, y)) { personGifts = true; return; }
        if (!PeopleView.Contains(x, y)) return;
        var people = People();
        for (int i = 0; i < people.Count; i++)
            if (PersonRect(i).Contains(x, y)) { personPick = people[i]; return; }
    }

    // ---------- Number pad ----------

    // Android's keyboard can't open on the bottom window (it never takes focus), so amounts get our own pad.
    int numpadMax;
    string numpadText = "";
    static readonly Rectangle NumpadCard = new(156, 76, 308, 444);
    static readonly string[] NumpadKeys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "Del", "0", "OK" };
    static Rectangle NumpadKey(int i) => new(172 + i % 3 * 96, 172 + i / 3 * 84, 88, 76);

    void OpenNumpad(int max)
    {
        numpadMax = Math.Max(1, max);
        numpadText = "";
    }

    void DrawNumpad(SpriteBatch b)
    {
        b.Draw(Game1.staminaRect, new Rectangle(0, 52, ModEntry.W, ModEntry.H), Color.Black * 0.45f);
        Card(b, NumpadCard.X, NumpadCard.Y, NumpadCard.Width, NumpadCard.Height);
        string shown = numpadText.Length > 0 ? numpadText : sellAmount.ToString();
        b.Draw(Game1.menuTexture, new Rectangle(172, 92, 280, 64), new Rectangle(128, 128, 64, 64), Color.White);
        Text(b, shown, new Vector2(188, 108), numpadText.Length > 0 ? Ink : Faint);
        RightText(b, $"max {numpadMax}", 440, 110, Faint);
        for (int i = 0; i < NumpadKeys.Length; i++)
        {
            var r = NumpadKey(i);
            Card(b, r.X, r.Y, r.Width, r.Height);
            var size = Game1.smallFont.MeasureString(NumpadKeys[i]);
            Text(b, NumpadKeys[i], new Vector2(r.X + (r.Width - size.X) / 2, r.Y + (r.Height - size.Y) / 2 + 2), i == 11 ? Color.DarkGreen : Ink);
        }
    }

    void TapNumpad(int x, int y)
    {
        if (!NumpadCard.Contains(x, y)) { numpadMax = 0; return; }  // tap outside cancels
        for (int i = 0; i < NumpadKeys.Length; i++)
        {
            if (!NumpadKey(i).Contains(x, y)) continue;
            string key = NumpadKeys[i];
            if (key == "Del") numpadText = numpadText.Length > 0 ? numpadText[..^1] : "";
            else if (key == "OK")
            {
                if (int.TryParse(numpadText, out var n)) sellAmount = Math.Clamp(n, 1, numpadMax);
                numpadMax = 0;
            }
            else if (numpadText.Length < 4) numpadText = (numpadText + key).TrimStart('0');
            Game1.playSound("smallSelect");
            return;
        }
    }

    // ---------- Bundle (Community Center note on top) ----------

    static Rectangle BundleCard(int i) => new(8 + i % 2 * 302, 98 + i / 2 * 72, 298, 66);
    static readonly Rectangle BundleBack = new(8, 466, 200, 62);

    void DrawBundle(SpriteBatch b, JunimoNoteMenu note)
    {
        if (!note.specificBundlePage)
        {
            // Room overview: the room's bundles as big cards, done ones dimmed with a check.
            Text(b, "Pick a bundle", new Vector2(22, 64), Faint);
            for (int i = 0; i < Math.Min(note.bundles.Count, 10); i++)
            {
                var bundle = note.bundles[i];
                var r = BundleCard(i);
                IClickableMenu.drawTextureBox(b, Game1.mouseCursors, BoxSrc, r.X, r.Y, r.Width, r.Height, bundle.complete ? new Color(200, 160, 120) : Color.White, 2f, false);
                if (bundle.complete) Icon(b, Game1.mouseCursors, CheckOn, r.X + 14, r.Y + 15, 4);
                Text(b, bundle.label, new Vector2(r.X + (bundle.complete ? 58 : 18), r.Y + 20), bundle.complete ? Faint : Ink);
            }
            return;
        }

        var page = note.currentPageBundle;
        Card(b, 8, 58, 604, 202);
        Text(b, page.label, new Vector2(22, 64));
        int filled = note.ingredientSlots.Count(c => c.item != null);
        RightText(b, $"{filled}/{page.numberOfIngredientSlots} given", 598, 64, Faint);
        // What it asks for: done ones get a check, like the slots on top.
        for (int i = 0; i < Math.Min(note.ingredientList.Count, 20); i++)
        {
            var it = note.ingredientList[i].item;
            int x = 22 + i % 10 * 58, y = 96 + i / 10 * 72;
            bool done = i < page.ingredients.Count && page.ingredients[i].completed;
            SlotFrame(b, x, y, 54);
            if (it != null)
            {
                Item(b, it, x + 3, y + 3, 48, done ? 0.35f : 1f);
                Count(b, it.Stack, x + 54, y + 54);
            }
            if (done) Icon(b, Game1.mouseCursors, CheckOn, x + 18, y + 18, 2);
        }

        Card(b, 8, 264, 604, 204);
        Text(b, "Bag - tap to give", new Vector2(22, 270), Faint);
        var items = Game1.player.Items;
        for (int i = 0; i < 36; i++)
        {
            var r = StoreBagRect(i);
            bool locked = i >= Game1.player.MaxItems;
            b.Draw(Game1.menuTexture, r, new Rectangle(128, 128, 64, 64), locked ? Color.White * 0.35f : Color.White);
            if (!locked && i < items.Count && items[i] is Item item)
            {
                bool usable = note.inventory.highlightMethod?.Invoke(item) ?? true;
                Item(b, item, r.X + 1, r.Y + 2, 48, usable ? 1f : 0.35f);
                Count(b, item.Stack, r.Right, r.Bottom);
            }
        }
        Card(b, BundleBack.X, BundleBack.Y, BundleBack.Width, BundleBack.Height);
        Icon(b, Game1.mouseCursors, ArrowLeft, BundleBack.X + 18, BundleBack.Y + 20, 2.5f);
        Text(b, "Back", new Vector2(BundleBack.X + 70, BundleBack.Y + 18));
        if (CurrentStatus is string st) Text(b, st, new Vector2(222, 484), Color.DarkRed);
    }

    void TapBundle(JunimoNoteMenu note, int x, int y)
    {
        if (!JunimoNoteMenu.canClick) return;
        if (!note.specificBundlePage)
        {
            for (int i = 0; i < Math.Min(note.bundles.Count, 10); i++)
                if (BundleCard(i).Contains(x, y) && !note.bundles[i].complete)
                    note.receiveLeftClick(note.bundles[i].bounds.Center.X, note.bundles[i].bounds.Center.Y);
            return;
        }
        if (BundleBack.Contains(x, y) && note.backButton != null && note.heldItem == null)
        {
            note.receiveLeftClick(note.backButton.bounds.Center.X, note.backButton.bounds.Center.Y);
            return;
        }
        int bag = Enumerable.Range(0, Game1.player.MaxItems).FirstOrDefault(n => StoreBagRect(n).Contains(x, y), -1);
        if (bag < 0 || note.heldItem != null) return;
        if (Game1.player.Items[bag] is Item it && !(note.inventory.highlightMethod?.Invoke(it) ?? true)) { Status("This bundle doesn't need that"); return; }

        // Same two clicks as on top: pick the item up, then drop it on the first empty slot that takes it.
        ClickSlot(note, note.inventory, bag);
        if (note.heldItem == null) return;
        var slot = note.ingredientSlots.FirstOrDefault(c => c.item == null && note.currentPageBundle.canAcceptThisItem(note.heldItem, c));
        if (slot != null) note.receiveLeftClick(slot.bounds.Center.X, slot.bounds.Center.Y);
        else Status("This bundle doesn't need that");
        // Whatever wasn't needed goes back into the bag.
        if (note.heldItem != null && Game1.player.addItemToInventoryBool(note.heldItem)) note.heldItem = null;
    }

    // ---------- End of day: shipping summary, level up ----------

    static Rectangle ShippedRow(int i) => new(8, 58 + i * 66, 604, 62);
    static readonly Rectangle EndContinue = new(352, 466, 260, 62), EndBack = new(8, 466, 200, 62);

    void DrawShipped(SpriteBatch b, ShippingMenu menu)
    {
        var totals = helper.Reflection.GetField<List<int>>(menu, "categoryTotals").GetValue();
        var items = helper.Reflection.GetField<List<List<Item>>>(menu, "categoryItems").GetValue();
        if (menu.currentPage >= 0)
        {
            // A category is open on top: list what was shipped in it.
            Card(b, 8, 58, 604, 400);
            Text(b, menu.getCategoryName(menu.currentPage), new Vector2(22, 64));
            var list = menu.currentPage < items.Count ? items[menu.currentPage] : new List<Item>();
            for (int i = 0; i < Math.Min(list.Count, 24); i++)
            {
                int x = 22 + i % 2 * 296, y = 98 + i / 2 * 30;
                Item(b, list[i], x, y - 4, 28);
                Text(b, $"{list[i].DisplayName} x{list[i].Stack}", new Vector2(x + 34, y), Faint);
            }
            Card(b, EndBack.X, EndBack.Y, EndBack.Width, EndBack.Height);
            Icon(b, Game1.mouseCursors, ArrowLeft, EndBack.X + 18, EndBack.Y + 20, 2.5f);
            Text(b, "Summary", new Vector2(EndBack.X + 70, EndBack.Y + 18));
            return;
        }
        // Summary: one row per category with what was shipped, then the day's total.
        for (int i = 0; i < Math.Min(totals.Count, 6); i++)
        {
            var r = ShippedRow(i);
            bool total = i == totals.Count - 1;
            Card(b, r.X, r.Y, r.Width, r.Height);
            Text(b, menu.getCategoryName(i), new Vector2(r.X + 16, r.Y + 18), total ? Ink : Faint);
            if (!total && i < items.Count)
                for (int k = 0; k < Math.Min(items[i].Count, 6); k++) Item(b, items[i][k], r.X + 180 + k * 40, r.Y + 13, 36);
            string g = $"{Utility.getNumberWithCommas(totals[i])}g";
            RightText(b, g, r.Right - 18, r.Y + 18, total ? Color.DarkGreen : Ink);
        }
        bool ready = helper.Reflection.GetField<int>(menu, "introTimer").GetValue() <= 0;
        IClickableMenu.drawTextureBox(b, Game1.mouseCursors, BoxSrc, EndContinue.X, EndContinue.Y, EndContinue.Width, EndContinue.Height, ready ? Color.White : new Color(200, 160, 120), 2f, false);
        Text(b, "Continue", new Vector2(EndContinue.X + 80, EndContinue.Y + 18), ready ? Ink : Faint);
    }

    void TapShipped(ShippingMenu menu, int x, int y)
    {
        if (menu.currentPage >= 0)
        {
            if (EndBack.Contains(x, y)) menu.currentPage = -1;
            return;
        }
        if (EndContinue.Contains(x, y)) { menu.receiveLeftClick(menu.okButton.bounds.Center.X, menu.okButton.bounds.Center.Y); return; }
        for (int i = 0; i < Math.Min(menu.categories.Count, 5); i++)
            if (ShippedRow(i).Contains(x, y))
                menu.receiveLeftClick(menu.categories[i].bounds.Center.X, menu.categories[i].bounds.Center.Y);
    }

    static readonly Rectangle LeftProfession = new(8, 150, 298, 300), RightProfession = new(314, 150, 298, 300);

    void DrawLevelUp(SpriteBatch b, LevelUpMenu menu)
    {
        string title = helper.Reflection.GetField<string>(menu, "title").GetValue();
        Card(b, 8, 58, 604, 86);
        Text(b, title ?? "Level up!", new Vector2(22, 66));
        if (!menu.isProfessionChooser)
        {
            var info = helper.Reflection.GetField<List<string>>(menu, "extraInfoForLevel").GetValue();
            Card(b, 8, 150, 604, 300);
            float y = 166;
            foreach (var line in info) Wrapped(b, line, 22, ref y, 576, Ink, 3);
            Card(b, EndContinue.X, EndContinue.Y, EndContinue.Width, EndContinue.Height);
            Text(b, "Continue", new Vector2(EndContinue.X + 80, EndContinue.Y + 18));
            return;
        }
        Text(b, "Choose a profession", new Vector2(22, 100), Faint);
        foreach (var (r, field) in new[] { (LeftProfession, "leftProfessionDescription"), (RightProfession, "rightProfessionDescription") })
        {
            var desc = helper.Reflection.GetField<List<string>>(menu, field).GetValue();
            Card(b, r.X, r.Y, r.Width, r.Height);
            float y = r.Y + 16;
            for (int i = 0; i < desc.Count; i++) Wrapped(b, desc[i], r.X + 16, ref y, r.Width - 32, i == 0 ? Color.DarkGreen : Ink, 4);
        }
    }

    /// <summary>The level-up menu ignores clicks and polls the pad itself, so run its own pick steps.</summary>
    void TapLevelUp(LevelUpMenu menu, int x, int y)
    {
        if (!menu.isActive || !menu.readyToClose()) return;
        if (!menu.isProfessionChooser)
        {
            if (EndContinue.Contains(x, y) && menu.informationUp) menu.okButtonClicked();
            return;
        }
        int pick = LeftProfession.Contains(x, y) ? 0 : RightProfession.Contains(x, y) ? 1 : -1;
        if (pick < 0) return;
        int profession = helper.Reflection.GetField<List<int>>(menu, "professionsToChoose").GetValue()[pick];
        Game1.player.professions.Add(profession);
        menu.getImmediateProfessionPerk(profession);
        menu.isActive = false;
        menu.informationUp = false;
        menu.isProfessionChooser = false;
        menu.RemoveLevelFromLevelList();
    }

    // ---------- Bag ----------

    // Slots fill the card's inner width (inside its 8px border) and are taller than wide: bigger targets,
    // same 12-column rows as the game.
    const int SlotW = 49, SlotH = 60, SlotX = 16;
    static readonly Rectangle TrashRect = new(8, 270, 176, 76), StackRect = new(192, 270, 420, 76);

    static Rectangle SlotRect(int i) => new(SlotX + i % Cols * SlotW, 66 + i / Cols * SlotH + (i >= Cols ? 8 : 0), SlotW, SlotH);

    // Short-lived feedback line ("Stored 12 items", "Bag is full"), cleared after a few seconds.
    string status;
    int statusUntil;

    void Status(string s)
    {
        status = s;
        statusUntil = Game1.ticks + 180;
    }

    string CurrentStatus => Game1.ticks < statusUntil ? status : null;

    // ---------- Bag: storage layout (chest open) ----------

    const int StoreSlotH = 52, StorePage = 36;
    int storePage;
    // Long-press amount picker in the chest view: which side, which slot (-1 = none).
    int storeIndex = -1;
    bool storeFromChest;
    static readonly Rectangle SplitMinus = new(68, 474, 48, 52), SplitPlus = new(172, 474, 48, 52),
        SplitMax = new(226, 474, 80, 52), SplitGo = new(314, 472, 298, 56);

    /// <summary>The open chest (or other storage) grid, when the top menu is an ItemGrabMenu.</summary>
    static InventoryMenu Storage => (Game1.activeClickableMenu as ItemGrabMenu)?.ItemsToGrabMenu;

    static Rectangle StoreRect(int i) => new(SlotX + i % Cols * SlotW, 94 + i / Cols * StoreSlotH, SlotW, StoreSlotH);
    static Rectangle StoreBagRect(int i) => new(SlotX + i % Cols * SlotW, 300 + i / Cols * StoreSlotH, SlotW, StoreSlotH);
    static readonly Rectangle FillRect = new(8, 472, 298, 56), OrganizeRect = new(314, 472, 298, 56);
    static readonly Rectangle StorePrev = new(500, 62, 40, 24), StoreNext = new(560, 62, 40, 24);

    void DrawStorage(SpriteBatch b, InventoryMenu store)
    {
        var chestItems = store.actualInventory;
        int pages = Math.Max(1, (store.capacity + StorePage - 1) / StorePage);
        storePage = Math.Min(storePage, pages - 1);

        Card(b, 8, 58, 604, 202);
        Text(b, "Chest - tap to take, hold for an amount", new Vector2(22, 64), Faint);
        if (pages > 1)
        {
            Text(b, $"{storePage + 1}/{pages}", new Vector2(440, 64), Faint);
            if (storePage > 0) Icon(b, Game1.mouseCursors, ArrowLeft, StorePrev.X + 6, StorePrev.Y + 2, 2);
            if (storePage < pages - 1) Icon(b, Game1.mouseCursors, ArrowRight, StoreNext.X + 6, StoreNext.Y + 2, 2);
        }
        for (int s = 0; s < StorePage; s++)
        {
            int i = storePage * StorePage + s;
            var r = StoreRect(s);
            bool exists = i < store.capacity;
            b.Draw(Game1.menuTexture, r, new Rectangle(128, 128, 64, 64), exists ? Color.White : Color.White * 0.35f);
            if (exists && i < chestItems.Count && chestItems[i] is Item item)
            {
                Item(b, item, r.X + 1, r.Y + 2, 48);
                Count(b, item.Stack, r.Right, r.Bottom);
            }
        }

        Card(b, 8, 264, 604, 204);
        Text(b, "Bag - tap to store, hold for an amount", new Vector2(22, 270), Faint);
        var items = Game1.player.Items;
        for (int i = 0; i < 36; i++)
        {
            var r = StoreBagRect(i);
            bool locked = i >= Game1.player.MaxItems;
            b.Draw(Game1.menuTexture, r, new Rectangle(128, 128, 64, 64), locked ? Color.White * 0.35f : Color.White);
            if (!locked && i < items.Count && items[i] is Item item)
            {
                Item(b, item, r.X + 1, r.Y + 2, 48);
                Count(b, item.Stack, r.Right, r.Bottom);
            }
        }

        if (StorePicked(store) is Item picked)
        {
            // Long-pressed slot: the button row becomes an amount picker.
            var pr = storeFromChest ? StoreRect(storeIndex - storePage * StorePage) : StoreBagRect(storeIndex);
            b.Draw(Game1.mouseCursors, pr, new Rectangle(194, 388, 16, 16), Color.White);
            Card(b, 8, 472, 298, 56);
            Item(b, picked, 16, 478, 44);
            foreach (var (r, label) in new[] { (SplitMinus, "-"), (SplitPlus, "+"), (SplitMax, "Max") })
            {
                var size = Game1.smallFont.MeasureString(label);
                Text(b, label, new Vector2(r.X + (r.Width - size.X) / 2, r.Y + (r.Height - size.Y) / 2 + 2));
            }
            var amt = sellAmount.ToString();
            Text(b, amt, new Vector2(144 - Game1.smallFont.MeasureString(amt).X / 2, 488));
            b.Draw(Game1.staminaRect, new Rectangle(124, 514, 40, 2), Faint);
            Card(b, SplitGo.X, SplitGo.Y, SplitGo.Width, SplitGo.Height);
            Text(b, $"{(storeFromChest ? "Take" : "Store")} {sellAmount}", new Vector2(SplitGo.X + 20, SplitGo.Y + 15));
            return;
        }

        // The chest menu's own side buttons, made big.
        Card(b, FillRect.X, FillRect.Y, FillRect.Width, FillRect.Height);
        Icon(b, Game1.mouseCursors, new Rectangle(103, 469, 16, 16), FillRect.X + 12, FillRect.Y + 12, 2);
        Text(b, "Add to stacks", new Vector2(FillRect.X + 54, FillRect.Y + 15));
        Card(b, OrganizeRect.X, OrganizeRect.Y, OrganizeRect.Width, OrganizeRect.Height);
        Icon(b, Game1.mouseCursors, new Rectangle(162, 440, 16, 16), OrganizeRect.X + 12, OrganizeRect.Y + 12, 2);
        Text(b, "Organize chest", new Vector2(OrganizeRect.X + 54, OrganizeRect.Y + 15));
    }

    Item StorePicked(InventoryMenu store)
    {
        if (storeIndex < 0) return null;
        var list = storeFromChest ? store.actualInventory : Game1.player.Items;
        var item = storeIndex < list.Count ? list[storeIndex] : null;
        if (item == null) storeIndex = -1;
        return item;
    }

    void TapStorage(InventoryMenu store, int x, int y)
    {
        var menu = Game1.activeClickableMenu;
        if (StorePicked(store) is Item picked && y >= 466)
        {
            if (new Rectangle(118, 474, 52, 52).Contains(x, y)) OpenNumpad(picked.Stack);
            else if (SplitMinus.Contains(x, y)) sellAmount = Math.Max(1, sellAmount - 1);
            else if (SplitPlus.Contains(x, y)) sellAmount = Math.Min(picked.Stack, sellAmount + 1);
            else if (SplitMax.Contains(x, y)) sellAmount = picked.Stack;
            else if (SplitGo.Contains(x, y)) MoveAmount(menu as ItemGrabMenu, store, picked);
            return;
        }
        storeIndex = -1;
        if (StorePrev.Contains(x, y)) { storePage = Math.Max(0, storePage - 1); return; }
        if (StoreNext.Contains(x, y)) { storePage++; return; }
        if (menu is ItemGrabMenu grab)
        {
            if (FillRect.Contains(x, y)) { grab.FillOutStacks(); Game1.playSound("Ship"); return; }
            if (OrganizeRect.Contains(x, y)) { ItemGrabMenu.organizeItemsInList(store.actualInventory); Game1.playSound("Ship"); return; }
        }
        // Click the matching slot in the game's own chest menu, so its rules decide what moves.
        int s = Enumerable.Range(0, StorePage).FirstOrDefault(n => StoreRect(n).Contains(x, y), -1);
        if (s >= 0) { ClickSlot(menu, store, storePage * StorePage + s); return; }
        int b = Enumerable.Range(0, Game1.player.MaxItems).FirstOrDefault(n => StoreBagRect(n).Contains(x, y), -1);
        if (b >= 0) ClickSlot(menu, OpenInventory, b);
    }

    /// <summary>Moves part of a stack between chest and bag; anything that doesn't fit stays put.</summary>
    void MoveAmount(ItemGrabMenu grab, InventoryMenu store, Item picked)
    {
        int count = Math.Clamp(sellAmount, 1, picked.Stack);
        var part = picked.getOne();
        part.Stack = count;
        Item left;
        if (storeFromChest) left = Game1.player.addItemToInventory(part);
        else if ((grab?.sourceItem ?? grab?.context) is StardewValley.Objects.Chest chest) left = chest.addItem(part);
        else { Status("Can't split into this storage"); return; }
        int moved = count - (left?.Stack ?? 0);
        if (moved == 0) { Status(storeFromChest ? "Bag is full" : "Chest is full"); return; }
        picked.Stack -= moved;
        if (picked.Stack <= 0)
        {
            var list = storeFromChest ? store.actualInventory : Game1.player.Items;
            list[storeIndex] = null;
            storeIndex = -1;
        }
        else sellAmount = Math.Min(sellAmount, picked.Stack);
        Game1.playSound("Ship");
    }

    static void ClickSlot(IClickableMenu menu, InventoryMenu grid, int index)
    {
        var slot = grid?.inventory.FirstOrDefault(c => int.TryParse(c.name, out var n) && n == index);
        if (slot != null) menu.receiveLeftClick(slot.bounds.Center.X, slot.bounds.Center.Y);
    }

    void DrawBag(SpriteBatch b)
    {
        if (Game1.activeClickableMenu is ItemGrabMenu { shippingBin: true })
        {
            var items0 = Game1.player.Items;
            if (sellPick >= 0 && (sellPick >= items0.Count || items0[sellPick] == null)) sellPick = -1;
            DrawMenuBag(b, "Shipping bin", sellPick >= 0 ? null : "Tap an item, choose how many, then Ship. It's sold overnight.",
                sellPick >= 0 ? null : Game1.getFarm().lastItemShipped, "Last shipped", null);
            if (sellPick >= 0) DrawAmountPicker(b, items0[sellPick], BinPickTop, "Ship", items0[sellPick].sellToStorePrice());
            return;
        }
        if (Game1.activeClickableMenu is GeodeMenu geode)
        {
            DrawMenuBag(b, "Clint - geodes", "Tap a geode to crack it open.", geode.geodeTreasure ?? lastGeodeFind, "Last found",
                $"25g each. You have {Utility.getNumberWithCommas(Game1.player.Money)}g");
            return;
        }
        if (Storage is InventoryMenu store) { DrawStorage(b, store); return; }
        // Any other menu with your inventory (museum, forge, tailoring...): grey out what it won't take, and
        // let taps go to its own click handling.
        if (Game1.activeClickableMenu is MenuWithInventory other) { DrawMenuBag(b, MenuTitle(other), MenuHint(other), null, "", null); return; }
        var menu = OpenInventory;
        Card(b, 8, 58, 604, 204);
        var items = Game1.player.Items;
        for (int i = 0; i < 36; i++)
        {
            var r = SlotRect(i);
            bool locked = i >= Game1.player.MaxItems;
            b.Draw(Game1.menuTexture, r, new Rectangle(128, 128, 64, 64), locked ? Color.White * 0.35f : Color.White);
            bool lifted = dragging && i == dragFrom;
            if (!locked && i < items.Count && items[i] is Item item)
            {
                Item(b, item, r.X + 1, r.Y + 6, 48, lifted ? 0.3f : 1f);
                if (!lifted) Count(b, item.Stack, r.Right, r.Bottom);
            }
            if (dragging && i == BagSlotAt(dragAt.X, dragAt.Y - ContentShift))
                b.Draw(Game1.staminaRect, r, Color.White * 0.4f);
            if (menu == null && i == Game1.player.CurrentToolIndex)
                b.Draw(Game1.mouseCursors, r, new Rectangle(194, 388, 16, 16), Color.White);
        }

        // Trash: a drop target, lit while an item hovers over it.
        bool overTrash = dragging && TrashRect.Contains(dragAt.X, dragAt.Y - ContentShift);
        Card(b, TrashRect.X, TrashRect.Y, TrashRect.Width, TrashRect.Height);
        if (overTrash) b.Draw(Game1.staminaRect, new Rectangle(TrashRect.X + 6, TrashRect.Y + 6, TrashRect.Width - 12, TrashRect.Height - 12), Color.Red * 0.25f);
        int lvl = Game1.player.trashCanLevel * 18;
        int tx = TrashRect.X, ty = TrashRect.Y;
        Icon(b, Game1.mouseCursors, new Rectangle(564 + lvl, 102, 18, 26), tx + 16, ty + 12, 2);
        b.Draw(Game1.mouseCursors, new Vector2(tx + 46, ty + 32), new Rectangle(564 + lvl, 129, 18, 10), Color.White,
            overTrash ? -0.6f : 0f, new Vector2(16, 10), 2f, SpriteEffects.None, 0);
        Text(b, "Trash", new Vector2(tx + 72, ty + 26), Faint);

        // Quick stack: top up stacks that already exist in this location's chests.
        Card(b, StackRect.X, StackRect.Y, StackRect.Width, StackRect.Height);
        Item(b, chest ??= ItemRegistry.Create("(BC)130"), StackRect.X + 14, StackRect.Y + 10, 56);
        Text(b, "Stack to chests", new Vector2(StackRect.X + 80, StackRect.Y + 12));
        Text(b, CurrentStatus ?? "Tops up stacks in chests", new Vector2(StackRect.X + 80, StackRect.Y + 38), Faint);

        // Item card: what you're holding.
        Card(b, 8, 354, 604, 174);
        var cur = Game1.player.CurrentItem;
        if (cur == null) { Text(b, "Tap an item to hold it. Drag to move or trash it.", new Vector2(22, 368), Faint); return; }
        SlotFrame(b, 22, 368, 64);
        Item(b, cur, 22, 368, 64);
        Text(b, cur.DisplayName, new Vector2(98, 370));
        if (cur is SObject o && o.sellToStorePrice() > 0)
        {
            Icon(b, Game1.mouseCursors, Coin, 98, 402, 2);
            Text(b, o.sellToStorePrice().ToString(), new Vector2(120, 398));
        }
        float y = 440;
        Wrapped(b, cur.getDescription().Replace('\n', ' '), 22, ref y, 576, Faint, 3);
    }

    static Item chest;

    static int BagSlotAt(int x, int y) =>
        Enumerable.Range(0, Game1.player.MaxItems).FirstOrDefault(n => SlotRect(n).Contains(x, y), -1);

    /// <summary>Drops a dragged item: trash, stack onto the same kind of item, or swap slots.</summary>
    void DropItem(int from, int x, int y)
    {
        var items = Game1.player.Items;
        if (from < 0 || items[from] is not Item moving) return;

        if (TrashRect.Contains(x, y))
        {
            if (!moving.canBeTrashed()) { Status($"{moving.DisplayName} can't be trashed"); return; }
            // The game's own trash: plays the sound and pays out the trash-can upgrade refund.
            Utility.trashItem(moving);
            items[from] = null;
            return;
        }

        int to = BagSlotAt(x, y);
        if (to < 0 || to == from) return;
        if (items[to] is Item there && there.canStackWith(moving))
        {
            int left = there.addToStack(moving);
            if (left > 0) moving.Stack = left;
            else items[from] = null;
            return;
        }
        items[from] = items[to];
        items[to] = moving;
    }

    /// <summary>Moves bag items into chests here that already hold the same kind; never the held item.</summary>
    void QuickStack()
    {
        var loc = Game1.currentLocation;
        var chests = loc.objects.Values.OfType<StardewValley.Objects.Chest>().Where(c => c.playerChest.Value).ToList();
        if (loc is FarmHouse house && house.fridge.Value != null) chests.Add(house.fridge.Value);
        if (chests.Count == 0) { Status("No chests here"); return; }

        var items = Game1.player.Items;
        int moved = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not Item item || i == Game1.player.CurrentToolIndex || item is Tool) continue;
            foreach (var c in chests)
            {
                if (!c.Items.Any(other => other != null && other.canStackWith(item))) continue;
                int before = item.Stack;
                var left = c.addItem(item);
                moved += before - (left?.Stack ?? 0);
                items[i] = left;
                if (left == null) break;
                item = left;
            }
        }
        if (moved > 0) Game1.playSound("Ship");
        Status(moved > 0 ? $"Stored {moved} item{(moved == 1 ? "" : "s")}" : "Nothing to stack");
    }

    void TapBag(int x, int y)
    {
        if (Game1.activeClickableMenu is ItemGrabMenu { shippingBin: true }) { TapBin(x, y); return; }
        if (Game1.activeClickableMenu is GeodeMenu) { TapMenuBag(x, y); return; }
        if (Storage is InventoryMenu store) { TapStorage(store, x, y); return; }
        if (Game1.activeClickableMenu is MenuWithInventory) { TapMenuBag(x, y); return; }
        if (StackRect.Contains(x, y) && OpenInventory == null) { QuickStack(); return; }
        int i = BagSlotAt(x, y);
        if (i < 0) return;

        var menu = OpenInventory;
        if (menu != null)
        {
            // Shop: click the matching slot so the game's own menu sells it.
            ClickSlot(Game1.activeClickableMenu, menu, i);
            return;
        }

        if (i < Cols) { Game1.player.CurrentToolIndex = i; return; }
        // Item sits in a back row: rotate the toolbar until it's on the hotbar, like the game's own row swap.
        var item = Game1.player.Items[i];
        if (item == null) return;
        for (int n = 0; n < Game1.player.MaxItems / Cols && Game1.player.Items.IndexOf(item) >= Cols; n++)
            Game1.player.shiftToolbar(true);
        int at = Game1.player.Items.IndexOf(item);
        if (at is >= 0 and < Cols) Game1.player.CurrentToolIndex = at;
    }

    // ---------- Craft ----------

    // Recipe grid scrolls vertically inside a fixed 3-row window.
    const int RecipeCols = 10, RecipeStep = 58, RecipeSize = 56;
    static readonly Rectangle GridView = new(16, 68, RecipeCols * RecipeStep, 3 * RecipeStep), CraftRect = new(446, 458, 152, 60);
    static readonly Rectangle ArrowUp = new(421, 459, 11, 12), ArrowDown = new(421, 472, 11, 12);
    int gridScroll, listStartY, listStartValue;
    bool listTouch, scrollingList;
    static readonly Rectangle ArrowLeft = new(352, 495, 12, 11), ArrowRight = new(365, 495, 12, 11);

    List<CraftingRecipe> recipes = new();
    readonly Dictionary<string, Item> recipeIcons = new();
    int recipeCount = -1;
    CraftingRecipe picked;

    Rectangle GridRect(int n) => new(GridView.X + n % RecipeCols * RecipeStep, GridView.Y + n / RecipeCols * RecipeStep - gridScroll, RecipeSize, RecipeSize);

    int GridCount => Showing == Tab.Shop && Game1.activeClickableMenu is ShopMenu shop ? shop.forSale.Count : recipes.Count;

    int MaxGridScroll => Showing == Tab.People
        ? Math.Max(0, People().Count * PeopleRowH - PeopleView.Height)
        : Math.Max(0, (GridCount + RecipeCols - 1) / RecipeCols * RecipeStep - GridView.Height);

    /// <summary>The scrolling window of whichever list is showing.</summary>
    Rectangle ListView => Showing == Tab.People ? PeopleView : GridView;

    /// <summary>Known crafting recipes in the game's own order, rebuilt only when you learn one.</summary>
    void RefreshRecipes()
    {
        if (Game1.player.craftingRecipes.Length == recipeCount) return;
        recipeCount = Game1.player.craftingRecipes.Length;
        recipes = CraftingRecipe.craftingRecipes.Keys.Where(Game1.player.craftingRecipes.ContainsKey)
            .Select(n => new CraftingRecipe(n, false)).ToList();
        gridScroll = Math.Min(gridScroll, MaxGridScroll);
    }

    Item RecipeIcon(CraftingRecipe r) => recipeIcons.TryGetValue(r.name, out var i) ? i : recipeIcons[r.name] = r.createItem();

    void DrawCraft(SpriteBatch b)
    {
        RefreshRecipes();
        Card(b, 8, 58, 604, 196);
        DrawGrid(b, recipes.Count, (n, r) =>
        {
            var recipe = recipes[n];
            bool can = recipe.doesFarmerHaveIngredientsInInventory();
            Item(b, RecipeIcon(recipe), r.X + 4, r.Y + 4, 48, can ? 1f : 0.35f);
            if (recipe == picked) b.Draw(Game1.mouseCursors, r, new Rectangle(194, 388, 16, 16), Color.White);
        });

        Card(b, 8, 262, 604, 266);
        if (recipes.Count == 0) { Text(b, "No crafting recipes yet.", new Vector2(22, 276), Faint); return; }
        if (picked == null) { Text(b, "Tap a recipe to see what it needs.", new Vector2(22, 276), Faint); return; }

        SlotFrame(b, 22, 274, 64);
        Item(b, RecipeIcon(picked), 22, 274, 64);
        Text(b, picked.DisplayName + (picked.numberProducedPerCraft > 1 ? $" x{picked.numberProducedPerCraft}" : ""), new Vector2(98, 276));
        float y = 302;
        Wrapped(b, picked.description, 98, ref y, 500, Faint, 2);

        // Ingredients: fixed 4x2 grid of icon + have/need.
        int k = 0;
        foreach (var (id, need) in picked.recipeList.Take(8))
        {
            int x = 22 + k % 4 * 146, iy = 356 + k / 4 * 46;
            var data = ItemRegistry.GetDataOrErrorItem(picked.getSpriteIndexFromRawIndex(id));
            Icon(b, data.GetTexture(), data.GetSourceRect(), x, iy, 2);
            int have = Game1.player.getItemCount(id);
            Text(b, $"{have}/{need}", new Vector2(x + 40, iy + 4), have >= need ? Color.DarkGreen : Color.DarkRed);
            k++;
        }

        bool can2 = picked.doesFarmerHaveIngredientsInInventory();
        IClickableMenu.drawTextureBox(b, Game1.mouseCursors, BoxSrc, CraftRect.X, CraftRect.Y, CraftRect.Width, CraftRect.Height, can2 ? Color.White : new Color(200, 160, 120), 2f, false);
        Item(b, RecipeIcon(picked), CraftRect.X + 10, CraftRect.Y + 12, 36, can2 ? 1f : 0.4f);
        Text(b, "Craft", new Vector2(CraftRect.X + 58, CraftRect.Y + 18), can2 ? Ink : Faint);
        if (CurrentStatus is string st) Text(b, st, new Vector2(22, 474), Color.DarkRed);
    }

    /// <summary>Scrolling 10-wide grid of slots clipped to GridView, with fade-and-arrow cues.</summary>
    void DrawGrid(SpriteBatch b, int count, Action<int, Rectangle> cell)
    {
        int rows = Math.Max(3, (count + RecipeCols - 1) / RecipeCols);
        DrawScrolled(b, GridView, () =>
        {
            for (int n = 0; n < rows * RecipeCols; n++)
            {
                var r = GridRect(n);
                if (r.Bottom < GridView.Y || r.Y > GridView.Bottom) continue;
                SlotFrame(b, r.X, r.Y, RecipeSize);
                if (n < count) cell(n, r);
            }
        });
    }

    /// <summary>Draws a list clipped to its window, then fade-and-arrow cues on edges with more past them.</summary>
    void DrawScrolled(SpriteBatch b, Rectangle view, Action draw)
    {
        // Clip so part-scrolled rows don't spill over the cards.
        var gd = b.GraphicsDevice;
        b.End();
        var oldScissor = gd.ScissorRectangle;
        gd.ScissorRectangle = new Rectangle(view.X, view.Y + ContentShift, view.Width, view.Height);
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, Clip, null, Matrix.CreateTranslation(0, ContentShift, 0));
        draw();
        b.End();
        gd.ScissorRectangle = oldScissor;
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, Matrix.CreateTranslation(0, ContentShift, 0));

        for (int side = 0; side < 2; side++)
        {
            if (side == 0 ? gridScroll <= 0 : gridScroll >= MaxGridScroll) continue;
            for (int f = 0; f < 20; f++)
            {
                int fy = side == 0 ? view.Y + f : view.Bottom - 1 - f;
                b.Draw(Game1.staminaRect, new Rectangle(view.X, fy, view.Width, 1), Paper * (1f - f / 20f));
            }
            Icon(b, Game1.mouseCursors, side == 0 ? ArrowUp : ArrowDown, ModEntry.W / 2 - 11, side == 0 ? view.Y - 2 : view.Bottom - 22, 2);
        }
    }

    static readonly RasterizerState Clip = new() { ScissorTestEnable = true, CullMode = CullMode.None };

    void TapCraft(int x, int y)
    {
        if (picked != null && CraftRect.Contains(x, y)) { Craft(picked); return; }
        if (!GridView.Contains(x, y)) return;
        for (int n = 0; n < recipes.Count; n++)
            if (GridRect(n).Contains(x, y)) { picked = recipes[n]; return; }
    }

    /// <summary>Same steps as the game's crafting page, but the result goes straight into the bag.</summary>
    void Craft(CraftingRecipe r)
    {
        if (!r.doesFarmerHaveIngredientsInInventory()) { Status("Missing ingredients"); return; }
        var item = r.createItem();
        if (!Game1.player.couldInventoryAcceptThisItem(item)) { Status("Bag is full"); return; }
        r.consumeIngredients(null);
        Game1.player.addItemToInventoryBool(item);
        Game1.player.NotifyQuests(q => q.OnRecipeCrafted(r, item));
        if (Game1.player.craftingRecipes.ContainsKey(r.name)) Game1.player.craftingRecipes[r.name] += r.numberProducedPerCraft;
        Game1.stats.checkForCraftingAchievements();
        Game1.playSound("coin");
    }

    // ---------- Tabs (settings) ----------

    const int SetRowH = 76;
    static Rectangle SetRow(int i) => new(8, 58 + i * SetRowH, 604, SetRowH - 6);
    static Rectangle SetShow(int i) => new(300, 58 + i * SetRowH, 120, SetRowH - 6);
    static Rectangle SetUp(int i) => new(430, 58 + i * SetRowH, 84, SetRowH - 6);
    static Rectangle SetDown(int i) => new(520, 58 + i * SetRowH, 84, SetRowH - 6);

    void DrawTabSettings(SpriteBatch b)
    {
        for (int i = 0; i < config.TabOrder.Count; i++)
        {
            var name = config.TabOrder[i];
            bool shown = !config.HiddenTabs.Contains(name);
            var r = SetRow(i);
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, BoxSrc, r.X, r.Y, r.Width, r.Height, shown ? Color.White : new Color(200, 160, 120), 2f, false);
            TabIcon(b, Enum.Parse<Tab>(name), r.X + 16, r.Y + 16);
            Text(b, name, new Vector2(r.X + 64, r.Y + 22), shown ? Ink : Faint);
            Icon(b, Game1.mouseCursors, shown ? CheckOn : CheckOff, SetShow(i).X, r.Y + 17, 4);
            Text(b, "Show", new Vector2(SetShow(i).X + 42, r.Y + 22), shown ? Ink : Faint);
            if (i > 0) Icon(b, Game1.mouseCursors, ArrowUp, SetUp(i).X + 26, r.Y + 17, 3);
            if (i < config.TabOrder.Count - 1) Icon(b, Game1.mouseCursors, ArrowDown, SetDown(i).X + 26, r.Y + 17, 3);
        }
    }

    void TapTabSettings(int x, int y)
    {
        for (int i = 0; i < config.TabOrder.Count; i++)
        {
            var name = config.TabOrder[i];
            if (SetShow(i).Contains(x, y))
            {
                if (!config.HiddenTabs.Remove(name)) config.HiddenTabs.Add(name);
            }
            else if (SetUp(i).Contains(x, y) && i > 0) (config.TabOrder[i - 1], config.TabOrder[i]) = (name, config.TabOrder[i - 1]);
            else if (SetDown(i).Contains(x, y) && i < config.TabOrder.Count - 1) (config.TabOrder[i + 1], config.TabOrder[i]) = (name, config.TabOrder[i + 1]);
            else continue;
            tabScroll = Math.Min(tabScroll, MaxTabScroll);
            helper.WriteConfig(config);
            Game1.playSound("drumkit6");
            return;
        }
    }

    // ---------- Choices (question dialogue on top) ----------

    const int ChoiceH = 78;
    static Rectangle ChoiceRect(int i) => new(8, 58 + i * ChoiceH, 604, ChoiceH - 6);

    void DrawChoices(SpriteBatch b)
    {
        var d = (DialogueBox)Game1.activeClickableMenu;
        for (int i = 0; i < Math.Min(d.responses.Length, 6); i++)
        {
            var r = ChoiceRect(i);
            Card(b, r.X, r.Y, r.Width, r.Height);
            float y = r.Y + (d.responses[i].responseText.Length > 40 ? 12 : 24);
            Wrapped(b, d.responses[i].responseText, r.X + 20, ref y, r.Width - 40, Ink, 2);
        }
    }

    static void TapChoices(int x, int y)
    {
        var d = (DialogueBox)Game1.activeClickableMenu;
        for (int i = 0; i < Math.Min(d.responses.Length, 6); i++)
        {
            if (!ChoiceRect(i).Contains(x, y)) continue;
            // Same as clicking the answer on top: first click finishes the typing, the next one answers.
            if (d.characterIndexInDialogue < d.getCurrentString().Length - 1) { d.receiveLeftClick(0, 0); return; }
            d.selectedResponse = i;
            d.receiveLeftClick(0, 0);
            return;
        }
    }

    // ---------- Shop ----------

    ISalable shopPick;
    int shopBagRow;
    // Selling: the picked bag slot and how many of it to sell.
    int sellPick = -1, sellAmount;
    // Amount picker, laid out from the top of the card it sits in (shop detail card, or the bin card).
    static Rectangle PickMinus(int top) => new(296, top + 8, 56, 52);
    static Rectangle PickPlus(int top) => new(436, top + 8, 56, 52);
    static Rectangle PickMax(int top) => new(500, top + 8, 100, 52);
    static Rectangle PickButton(int top) => new(296, top + 66, 304, 58);
    const int ShopPickTop = 262, BinPickTop = 92;
    static Rectangle PickRegion(int top) => new(290, top, 320, 130);
    static readonly Rectangle[] BuyRects = { new(272, 330, 106, 56), new(384, 330, 106, 56), new(496, 330, 106, 56) };
    static readonly int[] BuyCounts = { 1, 5, 25 };
    static Rectangle ShopBagRect(int c) => new(SlotX + c * SlotW, 436, SlotW, SlotH - 2);
    static readonly Rectangle ShopRowPrev = new(460, 404, 60, 28), ShopRowNext = new(540, 404, 60, 28);

    void DrawShop(SpriteBatch b)
    {
        var shop = (ShopMenu)Game1.activeClickableMenu;
        if (shopPick != null && !shop.forSale.Contains(shopPick)) shopPick = null;

        Card(b, 8, 58, 604, 196);
        DrawGrid(b, shop.forSale.Count, (n, r) =>
        {
            if (shop.forSale[n] is Item item) Item(b, item, r.X + 4, r.Y + 4, 48);
            if (shop.forSale[n] == shopPick) b.Draw(Game1.mouseCursors, r, new Rectangle(194, 388, 16, 16), Color.White);
        });

        Card(b, 8, 262, 604, 132);
        var items0 = Game1.player.Items;
        if (sellPick >= 0 && (sellPick >= items0.Count || items0[sellPick] == null)) sellPick = -1;
        if (sellPick >= 0) DrawAmountPicker(b, items0[sellPick], ShopPickTop, "Sell", SellUnitPrice(shop, items0[sellPick]));
        else if (shopPick == null) Text(b, "Tap an item to buy it, or a bag item to sell it.", new Vector2(22, 276), Faint);
        else
        {
            var stock = shop.itemPriceAndStock[shopPick];
            SlotFrame(b, 22, 270, 56);
            if (shopPick is Item pickItem) Item(b, pickItem, 22, 270, 56);
            Text(b, shopPick.DisplayName, new Vector2(90, 270));
            Icon(b, Game1.mouseCursors, Coin, 90, 302, 2);
            Text(b, stock.Price.ToString(), new Vector2(112, 298));
            if (stock.Stock != int.MaxValue) Text(b, $"{stock.Stock} left", new Vector2(182, 298), Faint);
            // The description the top screen shows in its tooltip, beside the Buy buttons.
            float dy = 330;
            Wrapped(b, shopPick.getDescription().Replace('\n', ' '), 22, ref dy, 244, Faint, 2);
            int money = ShopMenu.getPlayerCurrencyAmount(Game1.player, shop.currency);
            for (int i = 0; i < BuyRects.Length; i++)
            {
                var r = BuyRects[i];
                bool afford = money >= stock.Price * BuyCounts[i];
                IClickableMenu.drawTextureBox(b, Game1.mouseCursors, BoxSrc, r.X, r.Y, r.Width, r.Height, afford ? Color.White : new Color(200, 160, 120), 2f, false);
                string label = $"Buy {BuyCounts[i]}";
                Text(b, label, new Vector2(r.X + (r.Width - Game1.smallFont.MeasureString(label).X) / 2, r.Y + 16), afford ? Ink : Faint);
            }
        }

        // Selling: one bag row at a time; tap picks the item, the detail card above sells it.
        Card(b, 8, 402, 604, 126);
        int rows = Math.Max(1, Game1.player.MaxItems / Cols);
        shopBagRow = Math.Min(shopBagRow, rows - 1);
        Text(b, "Bag - tap to sell" + (rows > 1 ? $"  (row {shopBagRow + 1}/{rows})" : ""), new Vector2(22, 406), Faint);
        if (shopBagRow > 0) Icon(b, Game1.mouseCursors, ArrowLeft, ShopRowPrev.X + 18, ShopRowPrev.Y + 4, 2);
        if (shopBagRow < rows - 1) Icon(b, Game1.mouseCursors, ArrowRight, ShopRowNext.X + 18, ShopRowNext.Y + 4, 2);
        var items = Game1.player.Items;
        for (int c = 0; c < Cols; c++)
        {
            int i = shopBagRow * Cols + c;
            var r = ShopBagRect(c);
            b.Draw(Game1.menuTexture, r, new Rectangle(128, 128, 64, 64), Color.White);
            if (i < items.Count && items[i] is Item item)
            {
                // The shop greys out what it won't buy; match that.
                bool sellable = shop.inventory.highlightMethod?.Invoke(item) ?? true;
                Item(b, item, r.X + 1, r.Y + 6, 48, sellable ? 1f : 0.35f);
                Count(b, item.Stack, r.Right, r.Bottom);
            }
            if (i == sellPick) b.Draw(Game1.mouseCursors, r, new Rectangle(194, 388, 16, 16), Color.White);
        }
    }

    /// <summary>Item, price each, − / + / Max and a "{verb} N for Xg" button, from a card's top edge.</summary>
    void DrawAmountPicker(SpriteBatch b, Item item, int top, string verb, int unitPrice)
    {
        SlotFrame(b, 22, top + 12, 64);
        Item(b, item, 22, top + 12, 64);
        float ny = top + 10;
        Wrapped(b, item.DisplayName, 98, ref ny, 190, Ink, 2);
        Icon(b, Game1.mouseCursors, Coin, 98, top + 68, 2);
        Text(b, $"{unitPrice} each", new Vector2(120, top + 64), Faint);
        Text(b, $"Have {item.Stack}", new Vector2(98, top + 90), Faint);

        foreach (var (r, label) in new[] { (PickMinus(top), "-"), (PickPlus(top), "+"), (PickMax(top), "Max") })
        {
            Card(b, r.X, r.Y, r.Width, r.Height);
            var size = Game1.smallFont.MeasureString(label);
            Text(b, label, new Vector2(r.X + (r.Width - size.X) / 2, r.Y + (r.Height - size.Y) / 2 + 2));
        }
        var amt = sellAmount.ToString();
        Text(b, amt, new Vector2(394 - Game1.smallFont.MeasureString(amt).X / 2, top + 22));
        // Underlined: tap it to type an amount.
        b.Draw(Game1.staminaRect, new Rectangle(370, top + 48, 48, 2), Faint);

        var btn = PickButton(top);
        IClickableMenu.drawTextureBox(b, Game1.mouseCursors, BoxSrc, btn.X, btn.Y, btn.Width, btn.Height, Color.White, 2f, false);
        Icon(b, Game1.mouseCursors, Coin, btn.X + 14, btn.Y + 20, 2);
        Text(b, $"{verb} {sellAmount} for {unitPrice * sellAmount}g", new Vector2(btn.X + 40, btn.Y + 16));
    }

    /// <summary>Handles − / + / Max taps; returns true if the action button was tapped.</summary>
    bool TapAmountPicker(Item item, int top, int x, int y)
    {
        if (new Rectangle(356, top + 8, 76, 52).Contains(x, y)) OpenNumpad(item.Stack);
        else if (PickMinus(top).Contains(x, y)) sellAmount = Math.Max(1, sellAmount - 1);
        else if (PickPlus(top).Contains(x, y)) sellAmount = Math.Min(item.Stack, sellAmount + 1);
        else if (PickMax(top).Contains(x, y)) sellAmount = item.Stack;
        else return PickButton(top).Contains(x, y);
        return false;
    }

    int SellUnitPrice(ShopMenu shop, Item item) =>
        (int)(item.sellToStorePrice() * helper.Reflection.GetField<float>(shop, "sellPercentage").GetValue());

    /// <summary>Sells part of a stack, following the shop's own sell steps (minus the coin animation).</summary>
    void Sell(ShopMenu shop, int index, int count)
    {
        var items = Game1.player.Items;
        if (items[index] is not Item item || !(shop.inventory.highlightMethod?.Invoke(item) ?? true)) return;
        count = Math.Clamp(count, 1, item.Stack);
        var sold = item.getOne();
        sold.Stack = count;
        item.Stack -= count;
        if (item.Stack <= 0) items[index] = null;

        if (shop.onSell != null) shop.onSell(sold);
        else
        {
            int unit = SellUnitPrice(shop, sold);
            ShopMenu.chargePlayer(Game1.player, shop.currency, -unit * count);
            ISalable buyback = shop.CanBuyback() ? shop.AddBuybackItem(sold, unit, count) : null;
            // Shops resell edible items you sold them the next day.
            if (sold is SObject obj && obj.edibility.Value != -300)
            {
                var one = obj.getOne();
                one.Stack = count;
                if (buyback != null && shop.buyBackItemsToResellTomorrow.TryGetValue(buyback, out var existing)) existing.Stack += count;
                else if (Game1.currentLocation is ShopLocation shopLocation)
                {
                    if (buyback != null) shop.buyBackItemsToResellTomorrow[buyback] = one;
                    shopLocation.itemsToStartSellingTomorrow.Add(one);
                }
            }
            Game1.playSound("sell");
            Game1.playSound("purchase");
        }
        if (items[index] == null) sellPick = -1;
        else sellAmount = Math.Min(sellAmount, items[index].Stack);
    }

    void TapShop(int x, int y)
    {
        var shop = (ShopMenu)Game1.activeClickableMenu;
        if (ShopRowPrev.Contains(x, y)) { shopBagRow = Math.Max(0, shopBagRow - 1); return; }
        if (ShopRowNext.Contains(x, y)) { shopBagRow++; return; }
        for (int c = 0; c < Cols; c++)
        {
            int i = shopBagRow * Cols + c;
            if (!ShopBagRect(c).Contains(x, y) || i >= Game1.player.Items.Count || Game1.player.Items[i] is not Item it) continue;
            if (!(shop.inventory.highlightMethod?.Invoke(it) ?? true)) { Status($"{it.DisplayName} can't be sold here"); return; }
            sellPick = i; sellAmount = it.Stack; shopPick = null;
            return;
        }
        if (sellPick >= 0 && Game1.player.Items[sellPick] is Item selling && PickRegion(ShopPickTop).Contains(x, y))
        {
            if (TapAmountPicker(selling, ShopPickTop, x, y)) Sell(shop, sellPick, sellAmount);
            return;
        }
        if (shopPick != null)
            for (int i = 0; i < BuyRects.Length; i++)
                if (BuyRects[i].Contains(x, y)) { Buy(shop, shopPick, BuyCounts[i]); return; }
        if (!GridView.Contains(x, y)) return;
        for (int n = 0; n < shop.forSale.Count; n++)
            if (GridRect(n).Contains(x, y)) { shopPick = shop.forSale[n]; sellPick = -1; return; }
    }

    /// <summary>Buys through the shop's own purchase code, clamped like its shift/ctrl-click bulk buy.</summary>
    void Buy(ShopMenu shop, ISalable item, int count)
    {
        var stock = shop.itemPriceAndStock[item];
        int money = ShopMenu.getPlayerCurrencyAmount(Game1.player, shop.currency);
        count = Math.Min(count, Math.Max(1, money / Math.Max(1, stock.Price)));
        count = Math.Min(Math.Min(count, Math.Max(1, stock.Stock)), item.maximumStackSize() is > 0 and var max ? max : count);
        bool soldOut = helper.Reflection.GetMethod(shop, "tryToPurchaseItem").Invoke<bool>(item, shop.heldItem, count, 0, 0);
        if (soldOut)
        {
            shop.itemPriceAndStock.Remove(item);
            shop.forSale.Remove(item);
        }
        // The shop puts the purchase "in hand"; put it straight into the bag like gamepad mode does.
        if (shop.heldItem is Item bought && Game1.player.addItemToInventoryBool(bought)) shop.heldItem = null;
        else if (shop.heldItem != null) Status("Bag is full");
    }

    // ---------- Bag: shipping bin and geode layouts ----------

    void DrawMenuBag(SpriteBatch b, string title, string hint, Item shown, string shownLabel, string detail)
    {
        Card(b, 8, 58, 604, 202);
        Text(b, title, new Vector2(22, 64), Faint);
        float y = 96;
        if (hint != null || CurrentStatus != null) Wrapped(b, CurrentStatus ?? hint, 22, ref y, 576, Ink, 2);
        if (detail != null)
        {
            Icon(b, Game1.mouseCursors, Coin, 22, (int)y + 8, 2);
            Text(b, detail, new Vector2(44, y + 4), Faint);
        }
        if (shown != null)
        {
            Text(b, shownLabel, new Vector2(22, 178), Faint);
            SlotFrame(b, 22, 202, 48);
            Item(b, shown, 22, 202, 48);
            Text(b, shown.DisplayName, new Vector2(82, 214));
        }

        Card(b, 8, 264, 604, 204);
        Text(b, "Bag", new Vector2(22, 270), Faint);
        var menu = (MenuWithInventory)Game1.activeClickableMenu;
        var items = Game1.player.Items;
        for (int i = 0; i < 36; i++)
        {
            var r = StoreBagRect(i);
            bool locked = i >= Game1.player.MaxItems;
            b.Draw(Game1.menuTexture, r, new Rectangle(128, 128, 64, 64), locked ? Color.White * 0.35f : Color.White);
            if (!locked && i < items.Count && items[i] is Item item)
            {
                bool usable = menu.inventory.highlightMethod?.Invoke(item) ?? true;
                Item(b, item, r.X + 1, r.Y + 2, 48, usable ? 1f : 0.35f);
                Count(b, item.Stack, r.Right, r.Bottom);
            }
            if (i == sellPick && menu is ItemGrabMenu) b.Draw(Game1.mouseCursors, r, new Rectangle(194, 388, 16, 16), Color.White);
        }
    }

    static string MenuTitle(MenuWithInventory m) => m switch
    {
        MuseumMenu => "Museum - Gunther",
        ForgeMenu => "Forge",
        TailoringMenu => "Sewing machine",
        FieldOfficeMenu => "Field office",
        _ => "Bag",
    };

    static string MenuHint(MenuWithInventory m) => m switch
    {
        MuseumMenu => "Tap an item to donate it, then choose its spot on the top screen. Greyed-out items can't be donated.",
        ForgeMenu or TailoringMenu => "Tap an item to put it in, then finish on the top screen.",
        _ => "Tap an item to use it with the menu on the top screen.",
    };

    void TapBin(int x, int y)
    {
        var menu = (ItemGrabMenu)Game1.activeClickableMenu;
        var items = Game1.player.Items;
        if (sellPick >= 0 && items[sellPick] is Item picked && PickRegion(BinPickTop).Contains(x, y))
        {
            if (TapAmountPicker(picked, BinPickTop, x, y)) Ship(sellPick, sellAmount);
            return;
        }
        int i = Enumerable.Range(0, Game1.player.MaxItems).FirstOrDefault(n => StoreBagRect(n).Contains(x, y), -1);
        if (i < 0 || items[i] is not Item it) return;
        if (!(menu.inventory.highlightMethod?.Invoke(it) ?? true)) { Status($"{it.DisplayName} can't be shipped"); return; }
        sellPick = i; sellAmount = it.Stack;
    }

    /// <summary>Ships part or all of a stack through the farm's own shipItem (bin, last-shipped, animation).</summary>
    void Ship(int index, int count)
    {
        var items = Game1.player.Items;
        if (items[index] is not Item item) return;
        count = Math.Clamp(count, 1, item.Stack);
        var part = item;
        if (count < item.Stack)
        {
            part = item.getOne();
            part.Stack = count;
            item.Stack -= count;
        }
        Game1.getFarm().shipItem(part, Game1.player);
        Game1.playSound("Ship");
        if (items[index] == null) sellPick = -1;
        else sellAmount = Math.Min(sellAmount, items[index].Stack);
    }

    void TapMenuBag(int x, int y)
    {
        var menu = (MenuWithInventory)Game1.activeClickableMenu;
        int i = Enumerable.Range(0, Game1.player.MaxItems).FirstOrDefault(n => StoreBagRect(n).Contains(x, y), -1);
        if (i < 0) return;
        if (menu is GeodeMenu geode)
        {
            // One geode per tap: right-click picks up a single one, then drop it on the anvil.
            if (geode.geodeAnimationTimer > 0 || geode.heldItem != null) return;
            var slot = geode.inventory.inventory.FirstOrDefault(c => int.TryParse(c.name, out var n) && n == i);
            if (slot == null) return;
            geode.receiveRightClick(slot.bounds.Center.X, slot.bounds.Center.Y);
            if (geode.heldItem != null) geode.receiveLeftClick(geode.geodeSpot.bounds.Center.X, geode.geodeSpot.bounds.Center.Y);
            // Refused (bag full, under 25g): put it back rather than leave it in hand.
            if (geode.heldItem != null && geode.geodeAnimationTimer <= 0 && Game1.player.addItemToInventoryBool(geode.heldItem)) geode.heldItem = null;
            return;
        }
        ClickSlot(menu, menu.inventory, i);
    }

    // ---------- Title screen ----------

    // Title buttons at 3x (222x174 each) in a centered 2x2 grid; the top screen keeps only the logo.
    static Rectangle TitleButton(int i) => new((ModEntry.W - 456) / 2 + i % 2 * 234, (ModEntry.H - 360) / 2 + i / 2 * 186, 222, 174);
    static Rectangle SaveSlot(int i) => new(8, 16 + i * 104, 604, 96);
    static readonly Rectangle TitleBack = new(8, 452, 200, 76), SaveUp = new(520, 452, 44, 76), SaveDown = new(568, 452, 44, 76);

    /// <summary>The title screen's background at half the game's scale: same layers, same camera pan
    /// (viewportY) and the same night fade (globalXOffset) the game uses when Load opens.</summary>
    void DrawTitleBackground(SpriteBatch b, TitleMenu tm)
    {
        int w = ModEntry.W, h = ModEntry.H;
        const float z = 2f;
        float vy = tm.viewportY / 2f, night = tm.globalXOffset / 1200f;
        var clouds = tm.cloudsTexture;
        var big = helper.Reflection.GetField<List<float>>(tm, "bigClouds").GetValue();
        var small = helper.Reflection.GetField<List<float>>(tm, "smallClouds").GetValue();

        b.Draw(Game1.staminaRect, new Rectangle(0, 0, w, h), new Color(64, 136, 248));
        b.Draw(Game1.mouseCursors, new Rectangle(0, (int)(-300 * z - vy * 0.66f), w, (int)(300 * z + h - 120 * z)), new Rectangle(703, 1912, 1, 264), Color.White);
        for (int i = -10; i < w; i += 638)
            b.Draw(Game1.mouseCursors, new Vector2(i, -360 * z - vy * 0.66f), new Rectangle(0, 1453, 638, 195), Color.White * (1f - night), 0, Vector2.Zero, z, SpriteEffects.None, 0);
        foreach (float x in big)
            b.Draw(clouds, new Vector2(x / 2f, h - 250 * z - vy * 0.5f), new Rectangle(0, 0, 512, 337), Color.White * tm.globalCloudAlpha, 0, Vector2.Zero, z, SpriteEffects.None, 0);
        b.Draw(Game1.mouseCursors, new Vector2(-30 * z, h - 158 * z - vy * 0.66f), new Rectangle(0, 886, 639, 148), Color.White, 0, Vector2.Zero, z, SpriteEffects.None, 0);
        b.Draw(Game1.mouseCursors, new Vector2(-30 * z + 639 * z, h - 158 * z - vy * 0.66f), new Rectangle(0, 886, 640, 148), Color.White, 0, Vector2.Zero, z, SpriteEffects.None, 0);
        for (int j = 0; j < small.Count; j++)
        {
            var src = j % 3 == 0 ? new Rectangle(152, 447, 123, 55) : j % 3 == 1 ? new Rectangle(0, 471, 149, 66) : new Rectangle(410, 467, 63, 37);
            b.Draw(clouds, new Vector2(small[j] / 2f, h - 300 * z - j * 12 * z - vy * 0.5f), src, Color.White * tm.globalCloudAlpha, 0, Vector2.Zero, z, SpriteEffects.None, 0);
        }
        b.Draw(Game1.mouseCursors, new Vector2(0, h - 148 * z - vy), new Rectangle(0, 737, 639, 148), Color.White, 0, Vector2.Zero, z, SpriteEffects.None, 0);
        b.Draw(Game1.mouseCursors, new Vector2(639 * z, h - 148 * z - vy), new Rectangle(0, 737, 640, 148), Color.White, 0, Vector2.Zero, z, SpriteEffects.None, 0);
        b.Draw(clouds, new Vector2(0, h - 142 * z - vy * 2f), new Rectangle(0, 554, 165, 142), Color.White, 0, Vector2.Zero, z, SpriteEffects.None, 0);
        b.Draw(clouds, new Vector2(w - 122 * z, h - 153 * z - vy * 2f), new Rectangle(390, 543, 122, 153), Color.White, 0, Vector2.Zero, z, SpriteEffects.None, 0);

        // Night: fades in over everything as Load opens, out again on Back.
        if (night <= 0f) return;
        b.Draw(Game1.mouseCursors, new Rectangle(0, 0, w, h), new Rectangle(702, 1912, 1, 264), Color.White * night);
        var flip = SpriteEffects.None;
        for (int k = 0; k < h; k += 195 * 2)
        {
            for (int l = 0; l < w; l += 638 * 2)
                b.Draw(Game1.mouseCursors, new Vector2(l, k), new Rectangle(0, 1453, 638, 195), Color.White * night, 0, Vector2.Zero, z, flip, 0);
            flip = flip == SpriteEffects.None ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        }
    }

    /// <summary>Runs every tick: the buttons live on the bottom screen, so the top shows just the title.</summary>
    // Long press: finger down and still for half a second.
    Point pressAt;
    int pressTick;
    bool pressDown, longPressed;
    const int LongPressTicks = 30;

    public void Tick()
    {
        if (pressDown && Game1.ticks - pressTick >= LongPressTicks)
        {
            pressDown = false;
            longPressed = LongPress(pressAt.X, pressAt.Y - ContentShift);
        }
        if (!Context.IsWorldReady && Game1.activeClickableMenu is TitleMenu tm)
        {
            foreach (var button in tm.buttons.Take(4)) button.visible = false;
            TickTitleButtons(tm);
        }
        // The geode menu only holds the find while the crack animates; keep it for the panel.
        if (Game1.activeClickableMenu is GeodeMenu { geodeTreasure: Item found }) lastGeodeFind = found;
    }

    Item lastGeodeFind;

    /// <summary>Returns true if the long press did something, so the finger lifting isn't also a tap.</summary>
    bool LongPress(int x, int y)
    {
        if (Showing != Tab.Bag || Storage is not InventoryMenu store) return false;
        int s = Enumerable.Range(0, StorePage).FirstOrDefault(n => StoreRect(n).Contains(x, y), -1);
        int i = storePage * StorePage + s;
        if (s >= 0 && i < store.actualInventory.Count && store.actualInventory[i] is Item fromChest)
        {
            storeFromChest = true; storeIndex = i; sellAmount = Math.Max(1, fromChest.Stack / 2);
            return true;
        }
        int b = Enumerable.Range(0, Game1.player.MaxItems).FirstOrDefault(n => StoreBagRect(n).Contains(x, y), -1);
        if (b >= 0 && Game1.player.Items[b] is Item fromBag)
        {
            storeFromChest = false; storeIndex = b; sellAmount = Math.Max(1, fromBag.Stack / 2);
            return true;
        }
        return false;
    }

    void DrawTitle(SpriteBatch b, TitleMenu tm)
    {
        DrawTitleBackground(b, tm);
        switch (TitleMenu.subMenu)
        {
            case null:
                if (!TitleReady(tm)) { if (CanSkipIntro(tm)) DrawSkipHint(b); break; }
                for (int i = 0; i < Math.Min(Math.Min(tm.buttons.Count, 4), titleShown); i++)
                {
                    // Pop in: start a quarter bigger and settle over 12 ticks.
                    float t = Math.Clamp((Game1.ticks - titleShownAt[i]) / 12f, 0f, 1f);
                    var r = TitleButton(i);
                    float scale = 1f + 0.25f * (1f - t) * (1f - t);
                    var c = r.Center.ToVector2();
                    b.Draw(tm.titleButtonsTexture, c, tm.buttons[i].sourceRect, Color.White, 0f,
                        new Vector2(tm.buttons[i].sourceRect.Width / 2f, tm.buttons[i].sourceRect.Height / 2f), 3f * scale, SpriteEffects.None, 0);
                }
                break;

            case LoadGameMenu lm when lm.IsDoingTask():
                DrawLoadingStrip(b, Game1.content.LoadString("Strings\\StringsFromCSFiles:Game1.cs.3688"));
                break;

            case LoadGameMenu lm:
                var slots = lm.MenuSlots;
                for (int k = 0; k < LoadGameMenu.itemsPerPage && lm.currentItemIndex + k < slots.Count; k++)
                {
                    var r = SaveSlot(k);
                    Card(b, r.X, r.Y, r.Width, r.Height);
                    if (slots[lm.currentItemIndex + k] is LoadGameMenu.SaveFileSlot { Farmer: Farmer f })
                    {
                        // Same farmer pose as the game's load list, at half size. The renderer only places clothes
                        // right at scale 1, so draw at 1 and halve the whole batch; layers need depth sorting.
                        b.End();
                        b.Begin(SpriteSortMode.FrontToBack, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, Matrix.CreateScale(0.5f));
                        FarmerRenderer.isDrawingForUI = true;
                        f.FarmerRenderer.draw(b, new FarmerSprite.AnimationFrame(0, 0, false, false), 0, new Rectangle(0, 0, 16, 32),
                            new Vector2(r.X + 16, r.Y + 14) * 2, Vector2.Zero, 0.8f, 2, Color.White, 0f, 1f, f);
                        FarmerRenderer.isDrawingForUI = false;
                        b.End();
                        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
                        Text(b, f.Name, new Vector2(r.X + 64, r.Y + 14));
                        RightText(b, f.farmName.Value + " Farm", r.Right - 18, r.Y + 14, Faint);
                        string date = f.dayOfMonthForSaveGame.HasValue && f.seasonForSaveGame.HasValue && f.yearForSaveGame.HasValue
                            ? Utility.getDateStringFor(f.dayOfMonthForSaveGame.Value, f.seasonForSaveGame.Value, f.yearForSaveGame.Value)
                            : f.dateStringForSaveGame;
                        Text(b, date, new Vector2(r.X + 64, r.Y + 50), Faint);
                        string money = Utility.getNumberWithCommas(f.Money);
                        RightText(b, money, r.Right - 18, r.Y + 50, Faint);
                        Icon(b, Game1.mouseCursors, Coin, r.Right - 18 - (int)Game1.smallFont.MeasureString(money).X - 24, r.Y + 54, 2);
                    }
                }
                if (slots.Count == 0) { Card(b, 8, 16, 604, 96); Text(b, "No saves yet.", new Vector2(26, 50), Faint); }
                DrawTitleBack(b);
                if (lm.currentItemIndex > 0) { Card(b, SaveUp.X, SaveUp.Y, SaveUp.Width, SaveUp.Height); Icon(b, Game1.mouseCursors, ArrowUp, SaveUp.X + 11, SaveUp.Y + 26, 2); }
                if (lm.currentItemIndex + LoadGameMenu.itemsPerPage < slots.Count) { Card(b, SaveDown.X, SaveDown.Y, SaveDown.Width, SaveDown.Height); Icon(b, Game1.mouseCursors, ArrowDown, SaveDown.X + 11, SaveDown.Y + 26, 2); }
                break;

            default:
                // New game, co-op and the rest have text fields and pickers; keep those on top.
                Card(b, 110, 200, 400, 80);
                Text(b, "Continue on the top screen.", new Vector2(132, 228));
                DrawTitleBack(b);
                break;
        }
    }

    static void RightText(SpriteBatch b, string text, int right, int y, Color c) =>
        Text(b, text, new Vector2(right - Game1.smallFont.MeasureString(text).X, y), c);

    static void DrawTitleBack(SpriteBatch b)
    {
        Card(b, TitleBack.X, TitleBack.Y, TitleBack.Width, TitleBack.Height);
        Icon(b, Game1.mouseCursors, ArrowLeft, TitleBack.X + 18, TitleBack.Y + 26, 2.5f);
        Text(b, "Back", new Vector2(TitleBack.X + 70, TitleBack.Y + 26));
    }

    /// <summary>Taps click the matching control in the real title menu, so the game does the work.</summary>
    void TapTitle(TitleMenu tm, int x, int y)
    {
        switch (TitleMenu.subMenu)
        {
            case null:
                // During the intro any tap skips it, like clicking the top screen.
                if (!TitleReady(tm))
                {
                    if (CanSkipIntro(tm)) tm.receiveLeftClick(Game1.uiViewport.Width / 2, Game1.uiViewport.Height / 2);
                    return;
                }
                for (int i = 0; i < Math.Min(Math.Min(tm.buttons.Count, 4), titleShown); i++)
                    if (TitleButton(i).Contains(x, y))
                    {
                        // The top-screen copy is hidden, and hidden buttons ignore clicks; show it just for this one.
                        var button = tm.buttons[i];
                        button.visible = true;
                        tm.receiveLeftClick(button.bounds.Center.X, button.bounds.Center.Y);
                        button.visible = false;
                        return;
                    }
                break;
            case LoadGameMenu lm when !lm.IsDoingTask():
                if (SaveUp.Contains(x, y)) { lm.currentItemIndex = Math.Max(0, lm.currentItemIndex - 1); return; }
                if (SaveDown.Contains(x, y)) { lm.currentItemIndex = Math.Min(Math.Max(0, lm.MenuSlots.Count - LoadGameMenu.itemsPerPage), lm.currentItemIndex + 1); return; }
                for (int k = 0; k < LoadGameMenu.itemsPerPage && k < lm.slotButtons.Count; k++)
                    if (SaveSlot(k).Contains(x, y) && lm.currentItemIndex + k < lm.MenuSlots.Count)
                    { tm.receiveLeftClick(lm.slotButtons[k].bounds.Center.X, lm.slotButtons[k].bounds.Center.Y); return; }
                if (TitleBack.Contains(x, y)) ClickBack(tm);
                break;
            case not null when TitleBack.Contains(x, y):
                ClickBack(tm);
                break;
        }
    }

    // The title is always moving (clouds drift, the camera pans, night fades), so redraw it smoothly.
    static bool TitleAnimating => !Context.IsWorldReady && Game1.activeClickableMenu is TitleMenu;

    // Bottom-screen title buttons revealed so far, and the tick each appeared (for the pop-in).
    int titleShown;
    readonly int[] titleShownAt = new int[4];

    /// <summary>Reveals bottom title buttons one every 12 ticks once the title is ready. Follows the game's own
    /// count while it sequences (it plays the sound); plays the sound itself if the game skipped straight to all four.</summary>
    void TickTitleButtons(TitleMenu tm)
    {
        if (TitleMenu.subMenu != null || !TitleReady(tm)) { if (!TitleReady(tm)) titleShown = 0; return; }
        if (titleShown >= 4 || (titleShown > 0 && Game1.ticks - titleShownAt[titleShown - 1] < 12)) return;
        bool gameSequencing = tm.buttonsToShow < 4;
        if (gameSequencing && titleShown >= tm.buttonsToShow) return;
        titleShownAt[titleShown++] = Game1.ticks;
        if (!gameSequencing) Game1.playSound("Cowboy_gunshot");
    }

    /// <summary>"Tap to skip" on a card in the middle, bobbing and pulsing gently.</summary>
    static void DrawSkipHint(SpriteBatch b)
    {
        const string text = "Tap to skip";
        var size = Game1.smallFont.MeasureString(text);
        int w = (int)size.X + 48, h = 56;
        int bob = (int)Math.Round(Math.Sin(Game1.ticks / 12.0) * 4);
        int x = (ModEntry.W - w) / 2, y = ModEntry.H / 2 - h / 2 + bob;
        Card(b, x, y, w, h);
        float pulse = 0.75f + 0.25f * (float)Math.Sin(Game1.ticks / 8.0);
        Text(b, text, new Vector2(x + 24, y + (h - size.Y) / 2 + 2), Ink * pulse);
    }

    /// <summary>The only skip the game has: a click during the camera rise. Its logo can't be skipped (clicking it
    /// is an Easter egg), and the fade from white and the logo swipe ignore clicks.</summary>
    static bool CanSkipIntro(TitleMenu tm) =>
        tm.logoFadeTimer <= 0 && tm.fadeFromWhiteTimer <= 0 && !tm.titleInPosition && tm.logoSwipeTimer == 0f;

    /// <summary>The title is done animating in and takes clicks, the same checks the game uses.</summary>
    static bool TitleReady(TitleMenu tm) => tm.titleInPosition && tm.logoFadeTimer <= 0 && tm.fadeFromWhiteTimer <= 0;

    static void ClickBack(TitleMenu tm)
    {
        // Same guard the title menu uses for its own Back button.
        if (TitleMenu.subMenu?.readyToClose() ?? false) tm.backButtonPressed();
    }

    // ---------- Aim ----------

    // World pixels at the panel's top-left in the last Aim draw; maps taps back to tiles.
    xTile.Dimensions.Rectangle aimView;
    // 1.5x keeps whole screen pixels per art pixel (64-px tiles become 96 px) while making tiles bigger to tap.
    const float AimZoom = 1.5f;
    static readonly Matrix AimScale = Matrix.CreateScale(AimZoom);
    Vector2? aimTile;

    /// <summary>Draws the world around the player with the game's own map and object code,
    /// by pointing Game1.viewport at our panel for the duration.</summary>
    void DrawAim(SpriteBatch b)
    {
        var loc = Game1.currentLocation;
        var p = Game1.player.StandingPixel;
        int w0 = (int)(ModEntry.W / AimZoom), h0 = (int)(ModEntry.H / AimZoom);
        aimView = new xTile.Dimensions.Rectangle(p.X - w0 / 2, p.Y - (int)((AimBarY + TabH) / 2 / AimZoom), w0, h0);
        var old = Game1.viewport;
        b.End();
        // Some game draws (e.g. the swinging tool) go through Game1.spriteBatch directly, so
        // draw the world with it; it's idle here because we run between frames.
        var w = Game1.spriteBatch;
        try
        {
            Game1.viewport = aimView;
            w.GraphicsDevice.Clear(Color.Black);
            Game1.mapDisplayDevice.BeginScene(w);
            w.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, AimScale);
            foreach (var l in loc.backgroundLayers) l.Key.Draw(Game1.mapDisplayDevice, aimView, xTile.Dimensions.Location.Origin, false, 4, -1f);
            w.End();
            w.Begin(SpriteSortMode.FrontToBack, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, AimScale);
            loc.drawFloorDecorations(w);
            w.End();
            w.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, AimScale);
            for (int j = 0; j < loc.buildingLayers.Count; j++)
                loc.buildingLayers[j].Key.Draw(Game1.mapDisplayDevice, aimView, xTile.Dimensions.Location.Origin, false, 4,
                    loc.buildingLayers.Count > 1 ? 0.1f * j / (loc.buildingLayers.Count - 1) : 0f);
            w.End();
            w.Begin(SpriteSortMode.FrontToBack, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, AimScale);
            loc.draw(w);
            for (int k = 0; k < loc.frontLayers.Count; k++)
                loc.frontLayers[k].Key.Draw(Game1.mapDisplayDevice, aimView, xTile.Dimensions.Location.Origin, false, 4,
                    64f + (loc.frontLayers.Count > 1 ? 0.1f * k / (loc.frontLayers.Count - 1) : 0f));
            loc.drawAboveFrontLayer(w);
            w.End();
            foreach (var l in loc.alwaysFrontLayers)
            {
                w.Begin(SpriteSortMode.Texture, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, AimScale);
                l.Key.Draw(Game1.mapDisplayDevice, aimView, xTile.Dimensions.Location.Origin, false, 4, -1f);
                w.End();
            }
            DrawAimLighting(w, loc, old);
        }
        catch (Exception ex)
        {
            // A pass threw mid-batch; close it so the panel can still draw its overlay.
            try { w.End(); } catch (InvalidOperationException) { }
            monitor.LogOnce($"Aim world draw failed: {ex}", LogLevel.Error);
        }
        finally
        {
            Game1.viewport = old;
            Game1.mapDisplayDevice.BeginScene(Game1.spriteBatch);
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        }

        // Tile grid, then the targeted tile.
        float tile = 64 * AimZoom;
        // Dark line plus a light one beside it, so the grid reads on both grass and dark floors.
        Color dark = Color.Black * 0.45f, light = Color.White * 0.3f;
        for (float x = -Mod(aimView.X, 64) * AimZoom; x < ModEntry.W; x += tile)
        {
            b.Draw(Game1.staminaRect, new Rectangle((int)x, TabH, 1, AimBarY - TabH), dark);
            b.Draw(Game1.staminaRect, new Rectangle((int)x + 1, TabH, 1, AimBarY - TabH), light);
        }
        for (float y = -Mod(aimView.Y, 64) * AimZoom; y < AimBarY; y += tile)
        {
            b.Draw(Game1.staminaRect, new Rectangle(0, (int)y, ModEntry.W, 1), dark);
            b.Draw(Game1.staminaRect, new Rectangle(0, (int)y + 1, ModEntry.W, 1), light);
        }

        if (aimTile is Vector2 t)
        {
            var held = Game1.player.ActiveObject;
            // Held furniture shows its whole footprint, so you can see what it covers before placing.
            var hf = held as StardewValley.Objects.Furniture;
            int tw = hf?.getTilesWide() ?? 1, th = hf?.getTilesHigh() ?? 1;
            var r = new Rectangle((int)((t.X * 64 - aimView.X) * AimZoom), (int)((t.Y * 64 - aimView.Y) * AimZoom), (int)tile * tw, (int)tile * th);
            Color c = held != null && held.isPlaceable()
                ? (Utility.playerCanPlaceItemHere(loc, held, (int)t.X * 64 + 32, (int)t.Y * 64 + 32, Game1.player) ? Color.Lime : Color.Red)
                : Color.Yellow;
            b.Draw(Game1.staminaRect, r, c * 0.3f);
            const int edge = 3;
            b.Draw(Game1.staminaRect, new Rectangle(r.X, r.Y, r.Width, edge), c);
            b.Draw(Game1.staminaRect, new Rectangle(r.X, r.Bottom - edge, r.Width, edge), c);
            b.Draw(Game1.staminaRect, new Rectangle(r.X, r.Y, edge, r.Height), c);
            b.Draw(Game1.staminaRect, new Rectangle(r.Right - edge, r.Y, edge, r.Height), c);
        }

        DrawAimBar(b);

        if (Game1.player.ActiveObject is StardewValley.Objects.Furniture f && f.rotations.Value > 1)
        {
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, BoxSrc, RotateRect.X, RotateRect.Y, RotateRect.Width, RotateRect.Height, Color.White, 2f, false);
            Item(b, f, RotateRect.X + 8, RotateRect.Y + 8, 40);
            Text(b, "Rotate", new Vector2(RotateRect.X + 54, RotateRect.Y + 16));
        }
    }

    static readonly Rectangle RotateRect = new(456, AimBarY - 72, 156, 64);

    // Hotbar strip along the bottom of Aim, so you can swap tool or seed without leaving it.
    const int AimBarY = 478;
    static Rectangle AimSlot(int c) => new(SlotX + c * SlotW, AimBarY + 6, SlotW, 52);

    static void DrawAimBar(SpriteBatch b)
    {
        Card(b, 8, AimBarY - 2, 604, ModEntry.H - AimBarY + 2);
        var items = Game1.player.Items;
        for (int c = 0; c < Cols; c++)
        {
            var r = AimSlot(c);
            b.Draw(Game1.menuTexture, r, new Rectangle(128, 128, 64, 64), Color.White);
            if (c < items.Count && items[c] is Item item)
            {
                Item(b, item, r.X + 1, r.Y + 2, 48);
                Count(b, item.Stack, r.Right, r.Bottom);
            }
            if (c == Game1.player.CurrentToolIndex) b.Draw(Game1.mouseCursors, r, new Rectangle(194, 388, 16, 16), Color.White);
        }
    }

    static int Mod(int a, int m) => (a % m + m) % m;

    /// <summary>Lays the matching patch of the game's last lightmap over the Aim view, with its own blend,
    /// so night, caves and lamps look the same as on top. The Aim view is always inside the top screen's view.</summary>
    void DrawAimLighting(SpriteBatch w, GameLocation loc, xTile.Dimensions.Rectangle topView)
    {
        if (!Game1.drawLighting || Game1.lightmap == null) return;
        float zoom = Game1.options.zoomLevel;
        float scale = Game1.options.lightingQuality / 2f;
        if (Game1.game1.useUnscaledLighting) scale /= zoom;
        // World pixel -> top-screen pixel -> lightmap pixel.
        float k = zoom / scale;
        var src = new Rectangle((int)((aimView.X - topView.X) * k), (int)((aimView.Y - topView.Y) * k),
            (int)(aimView.Width * k), (int)(aimView.Height * k));
        var blend = helper.Reflection.GetField<BlendState>(Game1.game1, "lightingBlend").GetValue();
        w.Begin(SpriteSortMode.Deferred, blend, SamplerState.LinearClamp, null, null, null, AimScale);
        w.Draw(Game1.lightmap, new Rectangle(0, 0, aimView.Width, aimView.Height), src, Color.White);
        if (loc.IsOutdoors && loc.IsRainingHere())
            w.Draw(Game1.lightingRect, new Rectangle(0, 0, aimView.Width, aimView.Height), Color.OrangeRed * 0.45f);
        w.End();
    }

    /// <summary>Turns placed furniture to its next rotation that still fits; leaves it alone if none does.</summary>
    static void RotatePlaced(GameLocation loc, StardewValley.Objects.Furniture f)
    {
        if (!f.canBeRemoved(Game1.player)) return;
        int start = f.currentRotation.Value;
        loc.furniture.Remove(f);
        bool fits = false;
        do
        {
            f.rotate();
            if (f.currentRotation.Value == start) break;
            fits = f.canBePlacedHere(loc, f.TileLocation);
        } while (!fits);
        loc.furniture.Add(f);
        Game1.playSound(fits ? "dwop" : "cancel");
    }

    void TapAim(int x, int y)
    {
        // A menu on top owns the game; don't place or swing behind it.
        if (Game1.activeClickableMenu != null) return;
        if (y >= AimBarY)
        {
            int c = Enumerable.Range(0, Cols).FirstOrDefault(n => AimSlot(n).Contains(x, y), -1);
            if (c >= 0 && !Game1.player.UsingTool) Game1.player.CurrentToolIndex = c;
            return;
        }
        if (Game1.player.ActiveObject is StardewValley.Objects.Furniture hf && hf.rotations.Value > 1 && RotateRect.Contains(x, y))
        {
            hf.rotate();
            Game1.playSound("dwop");
            return;
        }
        var tile = new Vector2((int)Math.Floor((x / AimZoom + aimView.X) / 64), (int)Math.Floor((y / AimZoom + aimView.Y) / 64));
        int px = (int)tile.X * 64 + 32, py = (int)tile.Y * 64 + 32;
        var loc = Game1.currentLocation;
        var held = Game1.player.ActiveObject;

        // First tap picks the tile; tapping the same tile again acts on it.
        if (aimTile != tile) { aimTile = tile; return; }

        if (held != null && held.isPlaceable())
        {
            Utility.tryToPlaceItem(loc, held, px, py);
            return;
        }
        if (loc.GetFurnitureAt(tile) is StardewValley.Objects.Furniture placed && placed.rotations.Value > 1)
        {
            RotatePlaced(loc, placed);
            return;
        }
        if (Game1.player.CurrentTool != null && !Game1.player.UsingTool && Game1.player.CanMove)
        {
            // Tools hit the tile you face, so turn toward the target first, like the pad would.
            Game1.player.FacingDirection = Game1.player.getGeneralDirectionTowards(new Vector2(px, py));
            Game1.player.BeginUsingTool();
        }
    }
}
