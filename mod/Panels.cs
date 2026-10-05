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
    enum Tab { Today, Gifts, Bag, Aim }

    static readonly string[] TabNames = { "Today", "Gifts", "Bag", "Aim" };
    // Tab buttons fill the header row: about 24x10 mm each on the Thor's bottom screen.
    const int TabH = 64, TabW = 151, TabGap = 4, Slot = 48, Cols = 12;
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

    public int RedrawInterval => Showing switch { Tab.Aim => 2, Tab.Bag => 15, _ => 30 };

    /// <summary>Menu whose inventory the Bag panel drives, e.g. a chest or a shop.</summary>
    static InventoryMenu OpenInventory => Game1.activeClickableMenu switch
    {
        MenuWithInventory m => m.inventory,
        ShopMenu s => s.inventory,
        _ => null,
    };

    static bool Idle =>
        !Context.IsWorldReady || Game1.eventUp || Game1.dialogueUp
        || (Game1.activeClickableMenu != null && OpenInventory == null);

    Tab? Showing => Idle ? null : OpenInventory != null ? Tab.Bag : tab;

    public void Tap(int x, int y)
    {
        if (Idle) return;
        if (y < TabH)
        {
            int i = (x - TabGap) / (TabW + TabGap);
            if (OpenInventory == null && i < TabNames.Length) tab = (Tab)i;
            return;
        }
        if (Showing == Tab.Bag) TapBag(x, y - ContentShift);
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
            else DrawBag(b);
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
    static void DrawHeader(SpriteBatch b, Tab shown)
    {
        b.Draw(Game1.staminaRect, new Rectangle(0, 0, ModEntry.W, TabH), new Color(214, 147, 86));
        b.Draw(Game1.staminaRect, new Rectangle(0, TabH - 2, ModEntry.W, 2), Ink);
        for (int i = 0; i < TabNames.Length; i++)
        {
            bool on = (Tab)i == shown;
            int x = TabGap + i * (TabW + TabGap), y = on ? 6 : 2;
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, BoxSrc, x, y, TabW, TabH - 6, on ? Color.White : new Color(200, 160, 120), 2f, false);
            TabIcon(b, (Tab)i, x + 14, y + 12);
            Text(b, TabNames[i], new Vector2(x + 62, y + 16), on ? Ink : Faint);
        }
    }

    /// <summary>A 36x36 icon that says what the tab is for.</summary>
    static Item hoe;

    static void TabIcon(SpriteBatch b, Tab t, int x, int y)
    {
        switch (t)
        {
            case Tab.Today: WeatherIcon(b, Game1.weatherIcon, x, y + 6, 3); break;
            case Tab.Gifts: Icon(b, Game1.mouseCursors, Gift, x - 2, y - 2, 3); break;
            case Tab.Bag: Icon(b, Game1.mouseCursors, Backpack, x + 6, y - 2, 3.5f); break;
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
        item.drawInMenu(b, new Vector2(x - 32 * (1 - scale), y - 32 * (1 - scale)), scale, alpha, 0.9f, StackDrawType.Hide, Color.White, false);
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

    const int BagX = 22, BagY = 66;

    static Rectangle SlotRect(int i) => new(BagX + i % Cols * Slot, BagY + i / Cols * Slot + (i >= Cols ? 8 : 0), Slot, Slot);

    void DrawBag(SpriteBatch b)
    {
        var menu = OpenInventory;
        Card(b, 8, 58, 604, 176);
        var items = Game1.player.Items;
        for (int i = 0; i < 36; i++)
        {
            var r = SlotRect(i);
            bool locked = i >= Game1.player.MaxItems;
            b.Draw(Game1.menuTexture, r, new Rectangle(128, 128, 64, 64), locked ? Color.White * 0.35f : Color.White);
            if (!locked && i < items.Count && items[i] is Item item)
            {
                Item(b, item, r.X, r.Y);
                Count(b, item.Stack, r.Right, r.Bottom);
            }
            if (menu == null && i == Game1.player.CurrentToolIndex)
                b.Draw(Game1.mouseCursors, r, new Rectangle(194, 388, 16, 16), Color.White);
        }

        // Item card: what you're holding, or how taps work while a chest or shop is open.
        Card(b, 8, 242, 604, 290 - ContentShift);
        if (menu != null)
        {
            Text(b, Game1.activeClickableMenu is ShopMenu ? "Tap an item to sell it." : "Tap an item to move it into the chest.", new Vector2(22, 256), Faint);
            return;
        }
        var cur = Game1.player.CurrentItem;
        if (cur == null) { Text(b, "Tap an item to hold it.", new Vector2(22, 256), Faint); return; }
        SlotFrame(b, 22, 256, 64);
        Item(b, cur, 22, 256, 64);
        Text(b, cur.DisplayName, new Vector2(98, 258));
        if (cur is SObject o && o.sellToStorePrice() > 0)
        {
            Icon(b, Game1.mouseCursors, Coin, 98, 290, 2);
            Text(b, o.sellToStorePrice().ToString(), new Vector2(120, 286));
        }
        float y = 336;
        Wrapped(b, cur.getDescription().Replace('\n', ' '), 22, ref y, 576, Faint, 6);
    }

    void TapBag(int x, int y)
    {
        int i = Enumerable.Range(0, Game1.player.MaxItems).FirstOrDefault(n => SlotRect(n).Contains(x, y), -1);
        if (i < 0) return;

        var menu = OpenInventory;
        if (menu != null)
        {
            // Click the matching slot in the open chest/shop so the game's own menu logic moves or sells it.
            var slot = menu.inventory.FirstOrDefault(c => int.TryParse(c.name, out var n) && n == i);
            if (slot != null) Game1.activeClickableMenu.receiveLeftClick(slot.bounds.Center.X, slot.bounds.Center.Y);
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
            var r = new Rectangle((int)((t.X * 64 - aimView.X) * AimZoom), (int)((t.Y * 64 - aimView.Y) * AimZoom), (int)tile, (int)tile);
            var held = Game1.player.ActiveObject;
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
    }

    static int Mod(int a, int m) => (a % m + m) % m;

    void TapAim(int x, int y)
    {
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
        if (Game1.player.CurrentTool != null && !Game1.player.UsingTool && Game1.player.CanMove)
        {
            // Tools hit the tile you face, so turn toward the target first, like the pad would.
            Game1.player.FacingDirection = Game1.player.getGeneralDirectionTowards(new Vector2(px, py));
            Game1.player.BeginUsingTool();
        }
    }
}
