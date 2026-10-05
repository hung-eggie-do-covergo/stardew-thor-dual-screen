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

/// <summary>The three bottom-screen panels, drawn with the game's own art at 620x540.</summary>
public class Panels
{
    enum Tab { Today, Nearby, Bag }
    static readonly string[] TabNames = { "Today", "Nearby", "Bag" };

    const int TabH = 44, Pad = 12, Slot = 48, Cols = 12;
    static readonly Color Ink = new(86, 22, 12);
    static readonly Color Paper = new(255, 214, 147);
    static readonly Color Faint = new(150, 90, 60);

    Tab tab = Tab.Today;
    Texture2D logo;

    // "Who loves this" scans every villager, so only redo it when the held item changes.
    string lovedFor;
    List<string> lovedBy = new(), likedBy = new(), bundles = new();

    public int RedrawInterval => Showing == Tab.Bag ? 15 : 30;

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
            if (OpenInventory == null) tab = (Tab)Math.Clamp(x * TabNames.Length / ModEntry.W, 0, TabNames.Length - 1);
            return;
        }
        if (Showing == Tab.Bag) TapBag(x, y);
    }

    public void Draw(SpriteBatch b)
    {
        if (Showing is not Tab shown) { DrawLogo(b); return; }

        b.Draw(Game1.staminaRect, new Rectangle(0, 0, ModEntry.W, ModEntry.H), Paper);
        DrawTabs(b, shown);
        var area = new Rectangle(Pad, TabH + Pad, ModEntry.W - Pad * 2, ModEntry.H - TabH - Pad * 2);
        switch (shown)
        {
            case Tab.Today: DrawToday(b, area); break;
            case Tab.Nearby: DrawNearby(b, area); break;
            case Tab.Bag: DrawBag(b, area); break;
        }
    }

    // ---------- shared ----------

    void DrawLogo(SpriteBatch b)
    {
        logo ??= Game1.content.Load<Texture2D>("Minigames\\TitleButtons");
        var src = new Rectangle(0, 0, 398, 187);
        b.Draw(logo, new Vector2((ModEntry.W - src.Width) / 2, (ModEntry.H - src.Height) / 2), src, Color.White * 0.6f);
    }

    void DrawTabs(SpriteBatch b, Tab shown)
    {
        int w = ModEntry.W / TabNames.Length;
        for (int i = 0; i < TabNames.Length; i++)
        {
            bool on = (Tab)i == shown;
            b.Draw(Game1.staminaRect, new Rectangle(i * w, 0, w, TabH), on ? Paper : new Color(214, 147, 86));
            var size = Game1.smallFont.MeasureString(TabNames[i]);
            Text(b, TabNames[i], new Vector2(i * w + (w - size.X) / 2, (TabH - size.Y) / 2 + 2), on ? Ink : Faint);
        }
        b.Draw(Game1.staminaRect, new Rectangle(0, TabH - 2, ModEntry.W, 2), Ink);
    }

    static void Text(SpriteBatch b, string s, Vector2 at, Color? c = null) =>
        b.DrawString(Game1.smallFont, s, at, c ?? Ink);

    static float Line => Game1.smallFont.LineSpacing;

    static void Heading(SpriteBatch b, string s, ref float y, int x)
    {
        Text(b, s, new Vector2(x, y), Faint);
        y += Line;
    }

    static void Item(SpriteBatch b, Item item, int x, int y, float scale = 0.75f) =>
        item.drawInMenu(b, new Vector2(x - 8, y - 8), scale, 1f, 0.9f, StackDrawType.Hide, Color.White, false);

    static void Mugshot(SpriteBatch b, NPC npc, int x, int y)
    {
        var src = npc.getMugShotSourceRect();
        b.Draw(npc.Sprite.Texture, new Vector2(x, y), src, Color.White, 0, Vector2.Zero, 2f, SpriteEffects.None, 0);
    }

    // ---------- Today ----------

    void DrawToday(SpriteBatch b, Rectangle a)
    {
        float y = a.Y;
        Text(b, $"{Utility.getSeasonNameFromNumber(Game1.seasonIndex)} {Game1.dayOfMonth}, Year {Game1.year}", new Vector2(a.X, y));
        b.Draw(Game1.mouseCursors, new Vector2(a.Right - 52, y + 2), new Rectangle(317 + 12 * Game1.weatherIcon, 421, 12, 8), Color.White, 0, Vector2.Zero, 4f, SpriteEffects.None, 0);
        y += Line + 4;

        Text(b, $"Luck: {Luck(Game1.player.DailyLuck)}", new Vector2(a.X, y));
        Text(b, $"Tomorrow: {Weather(Game1.weatherForTomorrow)}", new Vector2(a.X + a.Width / 2, y));
        y += Line + 10;

        Heading(b, "Crops", ref y, a.X);
        var (ready, dry) = Crops();
        Text(b, ready == 0 && dry == 0 ? "Nothing to do" : $"{ready} ready to harvest   {dry} need water", new Vector2(a.X, y));
        y += Line + 10;

        Heading(b, "Coming up", ref y, a.X);
        foreach (var line in Events()) { Text(b, line, new Vector2(a.X, y)); y += Line; }
        y += 10;

        Heading(b, "Birthdays", ref y, a.X);
        var soon = Birthdays().ToList();
        if (soon.Count == 0) Text(b, "None this week", new Vector2(a.X, y));
        int x = a.X;
        foreach (var (npc, inDays) in soon)
        {
            if (x + 140 > a.Right) break;
            Mugshot(b, npc, x, (int)y);
            bool gifted = Game1.player.friendshipData.TryGetValue(npc.Name, out var f) && f.GiftsToday > 0;
            string when = inDays == 0 ? (gifted ? "Today, gifted" : "Today!") : inDays == 1 ? "Tomorrow" : $"In {inDays} days";
            Text(b, npc.displayName, new Vector2(x + 40, y));
            Text(b, when, new Vector2(x + 40, y + Line), inDays == 0 && !gifted ? Color.DarkRed : Faint);
            x += 200;
        }
    }

    static string Luck(double luck) => luck switch
    {
        > 0.07 => "Very good",
        > 0.02 => "Good",
        >= -0.02 => "Neutral",
        >= -0.07 => "Bad",
        _ => "Very bad",
    };

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

    static (int ready, int dry) Crops()
    {
        int ready = 0, dry = 0;
        foreach (var loc in new[] { Game1.getFarm(), Game1.getLocationFromName("Greenhouse") })
        {
            if (loc == null) continue;
            bool rain = loc.IsOutdoors && loc.IsRainingHere();
            var dirts = loc.terrainFeatures.Values.OfType<HoeDirt>()
                .Concat(loc.objects.Values.OfType<StardewValley.Objects.IndoorPot>().Select(p => p.hoeDirt.Value));
            foreach (var d in dirts)
            {
                if (d?.crop == null || d.crop.dead.Value) continue;
                if (d.readyForHarvest()) ready++;
                else if (!rain && d.needsWatering() && !d.isWatered()) dry++;
            }
        }
        return (ready, dry);
    }

    static IEnumerable<string> Events()
    {
        var festivals = DataLoader.Festivals_FestivalDates(Game1.temporaryContent);
        var lines = new List<string>();
        for (int i = 0; i <= 2; i++)
        {
            int day = Game1.dayOfMonth + i;
            if (day > 28) break;
            string when = i == 0 ? "Today" : i == 1 ? "Tomorrow" : "In 2 days";
            if (festivals.TryGetValue($"{Game1.currentSeason}{day}", out var name)) lines.Add($"{when}: {name}");
            if (day % 7 is 5 or 0) lines.Add($"{when}: Traveling cart");
        }
        if (Game1.dayOfMonth % 7 == 0) lines.Add("Today: new Queen of Sauce recipe");
        if (lines.Count == 0) lines.Add("Nothing special");
        return lines.Take(4);
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

    void DrawNearby(SpriteBatch b, Rectangle a)
    {
        float y = a.Y;
        var held = Game1.player.ActiveObject;
        if (held != null && !held.bigCraftable.Value)
        {
            RefreshHeld(held);
            Item(b, held, a.X, (int)y);
            Text(b, held.DisplayName, new Vector2(a.X + 52, y));
            Text(b, $"Sells for {held.sellToStorePrice()}g", new Vector2(a.X + 52, y + Line), Faint);
            y += 64;
            if (lovedBy.Count > 0) Wrapped(b, "Loved by " + string.Join(", ", lovedBy), a, ref y, Ink, 2);
            if (likedBy.Count > 0) Wrapped(b, "Liked by " + string.Join(", ", likedBy), a, ref y, Faint, 2);
            if (bundles.Count > 0) Wrapped(b, "Needed for " + string.Join(", ", bundles), a, ref y, Color.DarkGreen, 2);
            y += 12;
        }

        var npc = NearestVillager(8);
        if (npc == null)
        {
            Text(b, held == null ? "Hold an item or walk up to someone." : "Nobody nearby.", new Vector2(a.X, y), Faint);
            return;
        }

        Mugshot(b, npc, a.X, (int)y);
        Game1.player.friendshipData.TryGetValue(npc.Name, out var f);
        int hearts = (f?.Points ?? 0) / NPC.friendshipPointsPerHeartLevel;
        Text(b, $"{npc.displayName}   {hearts} hearts", new Vector2(a.X + 40, y));
        string gifts = f == null ? "Not met yet" : f.GiftsToday > 0 ? "Gifted today" : $"Gifts this week: {f.GiftsThisWeek}/2";
        if (npc.isBirthday()) gifts += "   Birthday today!";
        Text(b, gifts, new Vector2(a.X + 40, y + Line), Faint);
        y += Line * 2 + 8;

        if (held != null && npc.CanReceiveGifts())
        {
            Text(b, $"Your {held.DisplayName}: {Taste(npc.getGiftTasteForThisItem(held))}", new Vector2(a.X, y));
            y += Line + 4;
        }

        Heading(b, "Loves", ref y, a.X);
        int x = a.X;
        foreach (var item in Loves(npc))
        {
            if (x + Slot > a.Right) { x = a.X; y += Slot; }
            if (y + Slot > a.Bottom) break;
            Item(b, item, x, (int)y);
            x += Slot;
        }
    }

    /// <summary>Word-wraps to the panel width, cutting off after <paramref name="maxLines"/>.</summary>
    static void Wrapped(SpriteBatch b, string s, Rectangle a, ref float y, Color c, int maxLines)
    {
        var lines = Game1.parseText(s, Game1.smallFont, a.Width).Split('\n');
        for (int i = 0; i < Math.Min(lines.Length, maxLines); i++)
        {
            string line = i == maxLines - 1 && lines.Length > maxLines ? lines[i].TrimEnd(',', ' ') + "..." : lines[i];
            Text(b, line, new Vector2(a.X, y), c);
            y += Line;
        }
    }

    static string Taste(int t) => t switch
    {
        NPC.gift_taste_love => "loves it",
        NPC.gift_taste_like => "likes it",
        NPC.gift_taste_dislike => "dislikes it",
        NPC.gift_taste_hate => "hates it",
        _ => "neutral",
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
                if (t == NPC.gift_taste_love) lovedBy.Add(npc.displayName);
                else if (t == NPC.gift_taste_like) likedBy.Add(npc.displayName);
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

    static Rectangle SlotRect(Rectangle a, int i) => new(a.X + i % Cols * Slot, a.Y + 28 + i / Cols * (Slot + (i / Cols == 0 ? 8 : 0)), Slot, Slot);

    void DrawBag(SpriteBatch b, Rectangle a)
    {
        var menu = OpenInventory;
        string hint = menu == null ? "Tap to hold"
            : Game1.activeClickableMenu is ShopMenu ? "Tap to sell" : "Tap to move";
        Text(b, hint, new Vector2(a.X, a.Y), Faint);

        var items = Game1.player.Items;
        for (int i = 0; i < Game1.player.MaxItems; i++)
        {
            var r = SlotRect(a, i);
            b.Draw(Game1.menuTexture, r, new Rectangle(128, 128, 64, 64), Color.White);
            if (i < items.Count && items[i] is Item item)
            {
                Item(b, item, r.X, r.Y);
                if (item.Stack > 1) Utility.drawTinyDigits(item.Stack, b, new Vector2(r.Right - Utility.getWidthOfTinyDigitString(item.Stack, 2f) - 3, r.Bottom - 14), 2f, 1f, Color.White);
            }
            if (menu == null && i == Game1.player.CurrentToolIndex)
                b.Draw(Game1.mouseCursors, r, new Rectangle(194, 388, 16, 16), Color.White);
        }

        var cur = Game1.player.CurrentItem;
        if (cur != null)
        {
            float y = SlotRect(a, Game1.player.MaxItems - 1).Bottom + 16;
            Text(b, cur.DisplayName, new Vector2(a.X, y));
            y += Line;
            Wrapped(b, cur.getDescription().Replace('\n', ' '), a, ref y, Faint, 4);
        }
    }

    void TapBag(int x, int y)
    {
        var a = new Rectangle(Pad, TabH + Pad, ModEntry.W - Pad * 2, ModEntry.H - TabH - Pad * 2);
        int i = Enumerable.Range(0, Game1.player.MaxItems).FirstOrDefault(n => SlotRect(a, n).Contains(x, y), -1);
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
}
