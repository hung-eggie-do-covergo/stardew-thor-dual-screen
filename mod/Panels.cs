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
using SObject = StardewValley.Object;

namespace DualScreen;

/// <summary>The bottom-screen panels, drawn with the game's own UI sprites on a fixed 620x540 layout.</summary>
public class Panels
{
    enum Tab { Today, Gifts, Bag, Craft, Aim }

    static readonly string[] TabNames = { "Today", "Gifts", "Bag", "Craft", "Aim" };
    // Tabs keep a fixed size (about 24x10 mm on the Thor) and the strip scrolls sideways when they don't fit.
    const int TabH = 64, TabW = 140, TabGap = 4, Slot = 48, Cols = 12;
    // Content cards are laid out from y=58; shift them down to sit under the header.
    const int ContentShift = TabH + 6 - 58;

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

    public Panels(IMonitor monitor) => this.monitor = monitor;

    // "Who loves this" scans every villager, so only redo it when the held item changes.
    string lovedFor;
    List<NPC> lovedBy = new(), likedBy = new();
    List<string> bundles = new();

    public int RedrawInterval => Showing switch { Tab.Aim => 2, Tab.Bag => dragging ? 2 : 15, Tab.Craft => listTouch ? 2 : 30, _ => 30 };

    /// <summary>Menu whose inventory the Bag panel drives, e.g. a chest or a shop.</summary>
    static InventoryMenu OpenInventory => Game1.activeClickableMenu switch
    {
        MenuWithInventory m => m.inventory,
        ShopMenu s => s.inventory,
        _ => null,
    };

    // Panels stay up behind menus; only the title screen and cutscenes show the logo.
    static bool Idle => !Context.IsWorldReady || Game1.eventUp;

    Tab? Showing => Idle ? null : OpenInventory != null ? Tab.Bag : tab;

    // Header strip scroll, in pixels; swiping the header moves it.
    int tabScroll, scrollStartX, scrollStartValue;
    bool scrollingTabs, headerTouch;

    static int MaxTabScroll => Math.Max(0, TabGap + TabNames.Length * (TabW + TabGap) - ModEntry.W);

    // Bag drag-and-drop: the slot the finger went down on, and where it is now.
    int dragFrom = -1;
    Point dragAt;
    bool dragging;

    public void Touch(Android.Views.MotionEventActions action, int x, int y)
    {
        switch (action)
        {
            case Android.Views.MotionEventActions.Down when y < TabH:
                scrollStartX = x; scrollStartValue = tabScroll;
                headerTouch = true; scrollingTabs = false; dragFrom = -1; dragging = false;
                break;
            case Android.Views.MotionEventActions.Move when headerTouch:
                scrollingTabs |= Math.Abs(x - scrollStartX) > 12;
                if (scrollingTabs) tabScroll = Math.Clamp(scrollStartValue - (x - scrollStartX), 0, MaxTabScroll);
                break;
            case Android.Views.MotionEventActions.Down when Showing == Tab.Craft && RecipeView.Contains(x, y - ContentShift):
                listStartY = y; listStartValue = recipeScroll;
                listTouch = true; scrollingList = headerTouch = scrollingTabs = false;
                break;
            case Android.Views.MotionEventActions.Move when listTouch:
                scrollingList |= Math.Abs(y - listStartY) > 12;
                if (scrollingList) recipeScroll = Math.Clamp(listStartValue - (y - listStartY), 0, MaxRecipeScroll);
                break;
            case Android.Views.MotionEventActions.Down:
                headerTouch = scrollingTabs = listTouch = scrollingList = false;
                dragFrom = Showing == Tab.Bag && OpenInventory == null ? BagSlotAt(x, y - ContentShift) : -1;
                if (dragFrom >= 0 && Game1.player.Items[dragFrom] == null) dragFrom = -1;
                dragAt = new Point(x, y);
                dragging = false;
                break;
            case Android.Views.MotionEventActions.Move when dragFrom >= 0:
                // A few pixels of wobble still counts as a tap.
                dragging |= Math.Abs(x - dragAt.X) + Math.Abs(y - dragAt.Y) > 12;
                if (dragging) dragAt = new Point(x, y);
                break;
            case Android.Views.MotionEventActions.Up:
                if (dragging) DropItem(dragFrom, x, y - ContentShift);
                else if (!scrollingTabs && !scrollingList) Tap(x, y);
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
        if (Idle) return;
        if (y < TabH)
        {
            int i = (x + tabScroll - TabGap) / (TabW + TabGap);
            if (OpenInventory == null && i >= 0 && i < TabNames.Length) tab = (Tab)i;
            return;
        }
        if (Showing == Tab.Bag) TapBag(x, y - ContentShift);
        else if (Showing == Tab.Craft) TapCraft(x, y - ContentShift);
        else if (Showing == Tab.Aim) TapAim(x, y);
    }

    public void Draw(SpriteBatch b)
    {
        if (Showing is not Tab shown) { DrawLogo(b); return; }

        b.Draw(Game1.staminaRect, new Rectangle(0, 0, ModEntry.W, ModEntry.H), Paper);
        if (shown == Tab.Aim) DrawAim(b);
        else
        {
            b.End();
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, Matrix.CreateTranslation(0, ContentShift, 0));
            if (shown == Tab.Today) DrawToday(b);
            else if (shown == Tab.Gifts) DrawNearby(b);
            else if (shown == Tab.Craft) DrawCraft(b);
            else
            {
                DrawBag(b);
                // Last, so the dragged item floats over the item card; a bit larger to show past the fingertip.
                if (dragging && Game1.player.Items[dragFrom] is Item held)
                    Item(b, held, dragAt.X - 32, dragAt.Y - ContentShift - 32, 64);
            }
            b.End();
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        }
        DrawHeader(b, shown);
    }

    // ---------- shared ----------

    void DrawLogo(SpriteBatch b)
    {
        logo ??= Game1.content.Load<Texture2D>("Minigames\\TitleButtons");
        var src = new Rectangle(0, 0, 398, 187);
        b.Draw(logo, new Vector2((ModEntry.W - src.Width) / 2, (ModEntry.H - src.Height) / 2), src, Color.White * 0.6f);
    }

    /// <summary>Four labelled tab buttons across the top; the open one is lit and drops onto the page.</summary>
    void DrawHeader(SpriteBatch b, Tab shown)
    {
        b.Draw(Game1.staminaRect, new Rectangle(0, 0, ModEntry.W, TabH), new Color(214, 147, 86));
        b.Draw(Game1.staminaRect, new Rectangle(0, TabH - 2, ModEntry.W, 2), Ink);
        for (int i = 0; i < TabNames.Length; i++)
        {
            bool on = (Tab)i == shown;
            int x = TabGap + i * (TabW + TabGap) - tabScroll, y = on ? 6 : 2;
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, BoxSrc, x, y, TabW, TabH - 6, on ? Color.White : new Color(200, 160, 120), 2f, false);
            TabIcon(b, (Tab)i, x + 12, y + 12);
            Text(b, TabNames[i], new Vector2(x + 56, y + 16), on ? Ink : Faint);
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

        Portrait(b, npc, 330, 70);
        Text(b, npc.displayName, new Vector2(412, 72));
        Game1.player.friendshipData.TryGetValue(npc.Name, out var f);
        Hearts(b, (f?.Points ?? 0) / NPC.friendshipPointsPerHeartLevel, 412, 104);
        if (f == null) Text(b, "Not met yet", new Vector2(412, 120), Faint);
        else
        {
            // Same gift/week markers as the social page.
            Icon(b, Game1.mouseCursors, Gift, 412, 120, 2);
            Icon(b, Game1.mouseCursors, f.GiftsThisWeek >= 1 ? CheckOn : CheckOff, 446, 124, 2);
            Icon(b, Game1.mouseCursors, f.GiftsThisWeek >= 2 ? CheckOn : CheckOff, 468, 124, 2);
            if (npc.isBirthday()) Text(b, "Birthday!", new Vector2(496, 122), Color.DarkRed);
        }

        if (held != null && npc.CanReceiveGifts())
        {
            int t = npc.getGiftTasteForThisItem(held);
            Icon(b, Game1.mouseCursors, t == NPC.gift_taste_love || t == NPC.gift_taste_like ? HeartFull : HeartEmpty, 330, 160, 2);
            Text(b, Taste(t), new Vector2(350, 152), t == NPC.gift_taste_love ? Color.DarkGreen : t >= NPC.gift_taste_dislike && t != 8 ? Color.DarkRed : Ink);
        }

        Text(b, "Loves", new Vector2(330, 184), Faint);
        int i = 0;
        foreach (var item in Loves(npc).Take(24))
        {
            int x = 330 + i % 6 * 46, y = 210 + i / 6 * 46;
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
            if (npc.CanReceiveGifts())
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

    /// <summary>The open chest (or other storage) grid, when the top menu is an ItemGrabMenu.</summary>
    static InventoryMenu Storage => (Game1.activeClickableMenu as ItemGrabMenu)?.ItemsToGrabMenu;

    static Rectangle StoreRect(int i) => new(SlotX + i % Cols * SlotW, 92 + i / Cols * StoreSlotH, SlotW, StoreSlotH);
    static Rectangle StoreBagRect(int i) => new(SlotX + i % Cols * SlotW, 294 + i / Cols * StoreSlotH, SlotW, StoreSlotH);
    static readonly Rectangle FillRect = new(8, 466, 298, 62), OrganizeRect = new(314, 466, 298, 62);
    static readonly Rectangle StorePrev = new(500, 62, 40, 24), StoreNext = new(560, 62, 40, 24);

    void DrawStorage(SpriteBatch b, InventoryMenu store)
    {
        var chestItems = store.actualInventory;
        int pages = Math.Max(1, (store.capacity + StorePage - 1) / StorePage);
        storePage = Math.Min(storePage, pages - 1);

        Card(b, 8, 58, 604, 198);
        Text(b, "Chest - tap to take", new Vector2(22, 64), Faint);
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

        Card(b, 8, 260, 604, 198);
        Text(b, "Bag - tap to store", new Vector2(22, 266), Faint);
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

        // The chest menu's own side buttons, made big.
        Card(b, FillRect.X, FillRect.Y, FillRect.Width, FillRect.Height);
        Icon(b, Game1.mouseCursors, new Rectangle(103, 469, 16, 16), FillRect.X + 12, FillRect.Y + 15, 2);
        Text(b, "Add to stacks", new Vector2(FillRect.X + 54, FillRect.Y + 18));
        Card(b, OrganizeRect.X, OrganizeRect.Y, OrganizeRect.Width, OrganizeRect.Height);
        Icon(b, Game1.mouseCursors, new Rectangle(162, 440, 16, 16), OrganizeRect.X + 12, OrganizeRect.Y + 15, 2);
        Text(b, "Organize chest", new Vector2(OrganizeRect.X + 54, OrganizeRect.Y + 18));
    }

    void TapStorage(InventoryMenu store, int x, int y)
    {
        var menu = Game1.activeClickableMenu;
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

    static void ClickSlot(IClickableMenu menu, InventoryMenu grid, int index)
    {
        var slot = grid?.inventory.FirstOrDefault(c => int.TryParse(c.name, out var n) && n == index);
        if (slot != null) menu.receiveLeftClick(slot.bounds.Center.X, slot.bounds.Center.Y);
    }

    void DrawBag(SpriteBatch b)
    {
        if (Storage is InventoryMenu store) { DrawStorage(b, store); return; }
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

        // Item card: what you're holding, or how taps work while a chest or shop is open.
        Card(b, 8, 354, 604, 174);
        if (menu != null)
        {
            Text(b, "Tap an item to sell it.", new Vector2(22, 368), Faint);
            return;
        }
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
        if (Storage is InventoryMenu store) { TapStorage(store, x, y); return; }
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
    static readonly Rectangle RecipeView = new(16, 68, RecipeCols * RecipeStep, 3 * RecipeStep), CraftRect = new(446, 458, 152, 60);
    static readonly Rectangle ArrowUp = new(421, 459, 11, 12), ArrowDown = new(421, 472, 11, 12);
    int recipeScroll, listStartY, listStartValue;
    bool listTouch, scrollingList;
    static readonly Rectangle ArrowLeft = new(352, 495, 12, 11), ArrowRight = new(365, 495, 12, 11);

    List<CraftingRecipe> recipes = new();
    readonly Dictionary<string, Item> recipeIcons = new();
    int recipeCount = -1;
    CraftingRecipe picked;

    Rectangle RecipeRect(int n) => new(RecipeView.X + n % RecipeCols * RecipeStep, RecipeView.Y + n / RecipeCols * RecipeStep - recipeScroll, RecipeSize, RecipeSize);

    int MaxRecipeScroll => Math.Max(0, (recipes.Count + RecipeCols - 1) / RecipeCols * RecipeStep - RecipeView.Height);

    /// <summary>Known crafting recipes in the game's own order, rebuilt only when you learn one.</summary>
    void RefreshRecipes()
    {
        if (Game1.player.craftingRecipes.Length == recipeCount) return;
        recipeCount = Game1.player.craftingRecipes.Length;
        recipes = CraftingRecipe.craftingRecipes.Keys.Where(Game1.player.craftingRecipes.ContainsKey)
            .Select(n => new CraftingRecipe(n, false)).ToList();
        recipeScroll = Math.Min(recipeScroll, MaxRecipeScroll);
    }

    Item RecipeIcon(CraftingRecipe r) => recipeIcons.TryGetValue(r.name, out var i) ? i : recipeIcons[r.name] = r.createItem();

    void DrawCraft(SpriteBatch b)
    {
        RefreshRecipes();
        Card(b, 8, 58, 604, 196);
        // Clip the grid to its window so part-scrolled rows don't spill over the cards.
        var gd = b.GraphicsDevice;
        b.End();
        var oldScissor = gd.ScissorRectangle;
        gd.ScissorRectangle = new Rectangle(RecipeView.X, RecipeView.Y + ContentShift, RecipeView.Width, RecipeView.Height);
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, Clip, null, Matrix.CreateTranslation(0, ContentShift, 0));
        int rows = Math.Max(3, (recipes.Count + RecipeCols - 1) / RecipeCols);
        for (int n = 0; n < rows * RecipeCols; n++)
        {
            var r = RecipeRect(n);
            if (r.Bottom < RecipeView.Y || r.Y > RecipeView.Bottom) continue;
            SlotFrame(b, r.X, r.Y, RecipeSize);
            if (n >= recipes.Count) continue;
            var recipe = recipes[n];
            bool can = recipe.doesFarmerHaveIngredientsInInventory();
            Item(b, RecipeIcon(recipe), r.X + 4, r.Y + 4, 48, can ? 1f : 0.35f);
            if (recipe == picked) b.Draw(Game1.mouseCursors, r, new Rectangle(194, 388, 16, 16), Color.White);
        }
        b.End();
        gd.ScissorRectangle = oldScissor;
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, Matrix.CreateTranslation(0, ContentShift, 0));

        // Same cue as the tab strip: fade plus arrow on edges with more recipes past them.
        for (int side = 0; side < 2; side++)
        {
            if (side == 0 ? recipeScroll <= 0 : recipeScroll >= MaxRecipeScroll) continue;
            for (int f = 0; f < 20; f++)
            {
                int fy = side == 0 ? RecipeView.Y + f : RecipeView.Bottom - 1 - f;
                b.Draw(Game1.staminaRect, new Rectangle(RecipeView.X, fy, RecipeView.Width, 1), Paper * (1f - f / 20f));
            }
            Icon(b, Game1.mouseCursors, side == 0 ? ArrowUp : ArrowDown, ModEntry.W / 2 - 11, side == 0 ? RecipeView.Y - 2 : RecipeView.Bottom - 22, 2);
        }

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

    static readonly RasterizerState Clip = new() { ScissorTestEnable = true, CullMode = CullMode.None };

    void TapCraft(int x, int y)
    {
        if (picked != null && CraftRect.Contains(x, y)) { Craft(picked); return; }
        if (!RecipeView.Contains(x, y)) return;
        for (int n = 0; n < recipes.Count; n++)
            if (RecipeRect(n).Contains(x, y)) { picked = recipes[n]; return; }
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
        aimView = new xTile.Dimensions.Rectangle(p.X - w0 / 2, p.Y - (int)((ModEntry.H + TabH) / 2 / AimZoom), w0, h0);
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
            b.Draw(Game1.staminaRect, new Rectangle((int)x, TabH, 1, ModEntry.H - TabH), dark);
            b.Draw(Game1.staminaRect, new Rectangle((int)x + 1, TabH, 1, ModEntry.H - TabH), light);
        }
        for (float y = -Mod(aimView.Y, 64) * AimZoom; y < ModEntry.H; y += tile)
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

        if (Game1.player.ActiveObject is StardewValley.Objects.Furniture f && f.rotations.Value > 1)
        {
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, BoxSrc, RotateRect.X, RotateRect.Y, RotateRect.Width, RotateRect.Height, Color.White, 2f, false);
            Item(b, f, RotateRect.X + 8, RotateRect.Y + 8, 40);
            Text(b, "Rotate", new Vector2(RotateRect.X + 54, RotateRect.Y + 16));
        }
    }

    static readonly Rectangle RotateRect = new(456, 468, 156, 64);

    static int Mod(int a, int m) => (a % m + m) % m;

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
