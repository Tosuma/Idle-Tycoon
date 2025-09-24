using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdleTycoon;


// Entry point
public static class Program
{
    public static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.CursorVisible = false;
        Directory.CreateDirectory("saves");
        var catalog = ItemCatalog.Default();
        var settings = SettingsSystem.Load("saves/settings.json");

        while (true)
        {
            var choice = StartMenu.Run();
            switch (choice)
            {
                case StartMenu.Option.NewGame:
                    var newState = GameState.New();
                    new Game(newState, catalog, settings).Run();
                    break;
                case StartMenu.Option.Continue:
                    var loaded = SaveSystem.TryLoad("saves/slot1.json");
                    if (loaded != null)
                        new Game(loaded, catalog, settings).Run();
                    else
                        UI.Toast("No save found. Start a new game first.");
                    break;
                case StartMenu.Option.Settings:
                    SettingsMenu.Run(settings);
                    SettingsSystem.Save(settings, "saves/settings.json");
                    break;
                case StartMenu.Option.Exit:
                    UI.Cleanup();
                    return;
            }
        }
    }
}

#region Start Menu
public static class StartMenu
{
    public enum Option { Continue, NewGame, Settings, Exit }

    public static Option Run()
    {
        using var __page = UI.Page();
        string title = "Idle Tycoon";
        var options = new[] { Option.Continue, Option.NewGame, Option.Settings, Option.Exit };
        int idx = 0;

        while (true)
        {
            UI.Clear();
            UI.WriteCentered(1, title, ConsoleColor.Cyan, bold: true);
            UI.WriteCentered(3, "Use ↑/↓ to move, Enter to select", ConsoleColor.DarkGray);

            int startRow = 6;
            for (int i = 0; i < options.Length; i++)
            {
                bool selected = (i == idx);
                string text = options[i] switch
                {
                    Option.Continue => "Continue",
                    Option.NewGame => "New Game",
                    Option.Settings => "Settings",
                    Option.Exit => "Exit",
                    _ => options[i].ToString()!
                };
                UI.WriteCentered(startRow + i * 2, selected ? $"> {text} <" : text,
                    selected ? ConsoleColor.Black : ConsoleColor.White,
                    bg: selected ? ConsoleColor.Yellow : (ConsoleColor?)null);
            }

            var key = Console.ReadKey(true).Key;
            if (key == ConsoleKey.UpArrow) idx = (idx - 1 + options.Length) % options.Length;
            else if (key == ConsoleKey.DownArrow) idx = (idx + 1) % options.Length;
            else if (key == ConsoleKey.Enter) return options[idx];
        }
    }
}
#endregion

#region Game Core
public sealed class Game
{
    private readonly Settings _settings;
    private readonly GameState _state;

    private readonly ItemCatalog _catalog;

    // Cached layout positions so we can do partial redraws
    private int _width, _height;
    private int _moneyRow = 1, _moneyCol = 2;
    private int _prodRow = 1, _prodCol;
    private int _ownedHeaderRow = 4, _ownedStartRow = 6;
    private int _bottomRow;

    // Dirty flags
    private bool _layoutDirty = true;      // window resized or first draw
    private bool _ownedListDirty = true;   // purchases changed list

    // Last displayed values to avoid redundant writes
    private double _lastMoneyDrawn = double.NaN;
    private double _lastProdDrawn = double.NaN;

    public Game(GameState state, ItemCatalog catalog, Settings settings)
    {
        _state = state;
        _catalog = catalog;
        _settings = settings;
    }

    public void Run()
    {
        var watch = Stopwatch.StartNew();
        double last = watch.Elapsed.TotalSeconds;
        bool running = true;
        string AutosaveLabel() => _settings.AutosaveSeconds > 0 ? $"Autosave: {_settings.AutosaveSeconds}s" : "Autosave: Off";
        var bottomMenu = new[] {
            "[S]hop", "[U] Upgrades", "[F5] Save", "[P] Prestige", "[Esc] Exit to Menu", "[Space] Collect (if any click mechanic)",
            _settings.AutosaveSeconds > 0 ? $"Autosave: {_settings.AutosaveSeconds}s" : "Autosave: Off"
        };

        // Initial layout sizing
        _width = Console.WindowWidth;
        _height = Console.WindowHeight;

        DrawStaticHUD(bottomMenu);
        DrawOwnedList(force: true);
        UpdateMoneyAndProd(force: true);

        double autosaveTimer = 0;
        while (running)
        {
            // If a page cleared the screen on exit (older builds), force a full layout redraw
            if (Console.CursorLeft == 0 && Console.CursorTop == 0 && !_layoutDirty)
                _layoutDirty = true;
            // Tick economy
            double now = watch.Elapsed.TotalSeconds;
            double dt = now - last; last = now;
            if (dt > 0) { Tick(dt); autosaveTimer += dt; }

            // Autosave
            if (_settings.AutosaveSeconds > 0 && autosaveTimer >= _settings.AutosaveSeconds)
            {
                SaveSystem.Save(_state, "saves/slot1.json");
                autosaveTimer = 0;
                UI.Toast($"Autosaved.");
            }

            // Detect resize
            if (_width != Console.WindowWidth || _height != Console.WindowHeight)
            {
                _width = Console.WindowWidth;
                _height = Console.WindowHeight;
                _layoutDirty = true;
            }

            // Redraw layout if needed
            if (_layoutDirty)
            {
                DrawStaticHUD(bottomMenu);
                DrawOwnedList(force: true);
                UpdateMoneyAndProd(force: true);
                _layoutDirty = false;
            }
            else
            {
                // Fast-path updates: only the counters
                UpdateMoneyAndProd(force: false);

                if (_ownedListDirty)
                {
                    DrawOwnedList(force: true);
                    _ownedListDirty = false;
                }
            }

            // Input
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true).Key;
                switch (key)
                {
                    case ConsoleKey.S:
                        OpenShop();
                        break;
                    case ConsoleKey.U:
                        OpenUpgrades();
                        break;
                    case ConsoleKey.F5:
                        SaveSystem.Save(_state, "saves/slot1.json");
                        UI.Toast("Game saved.");
                        break;
                    case ConsoleKey.Spacebar:
                        _state.Money += 1.0; _state.LifetimeEarnings += 1.0;
                        break;
                    case ConsoleKey.P:
                        OpenPrestige();
                        break;
                    case ConsoleKey.Escape:
                        running = ConfirmExit();
                        break;
                }
            }

            UI.TickToasts();
            System.Threading.Thread.Sleep(33); // ~30 FPS polling
        }
    }

    private bool ConfirmExit()
    {
        UI.Toast("Press Esc again to exit, any other key to stay.");
        var k = Console.ReadKey(true).Key;
        return k != ConsoleKey.Escape; // false => exit loop
    }

    private void Tick(double dt)
    {
        double perSec = _state.TotalProductionPerSecond(_catalog);
        _state.Money += perSec * dt;
        _state.LifetimeEarnings += perSec * dt;
    }

    private void DrawStaticHUD(string[] bottomMenu)
    {
        UI.Clear();
        _prodCol = Math.Max(0, _width / 2);

        // Header bars
        UI.Write(0, 0, new string('─', Math.Max(0, _width - 1)), ConsoleColor.DarkGray);
        UI.Write(_moneyRow, 0, new string(' ', Math.Max(0, _width - 1))); // clear line for fields
        UI.Write(2, 0, new string('─', Math.Max(0, _width - 1)), ConsoleColor.DarkGray);

        // Static labels (values filled by UpdateMoneyAndProd)
        UI.Write(_moneyRow, _moneyCol, "Money: ");
        UI.Write(_prodRow, _prodCol, "Prod/s: ");

        // Owned header & prestige info
        UI.Write(_ownedHeaderRow - 1, 2, $"Prestige: {_state.PrestigeCredits} (x{Prestige.ProdMultiplier(_state.PrestigeCredits):0.00})", ConsoleColor.DarkYellow);
        UI.Write(_ownedHeaderRow, 2, "Owned Producers:", ConsoleColor.Cyan);

        // Bottom menu & divider
        _bottomRow = _height - 4;
        if (_bottomRow < _ownedStartRow + 2) _bottomRow = _ownedStartRow + 2;
        UI.Write(_bottomRow, 0, new string('─', Math.Max(0, _width - 1)), ConsoleColor.DarkGray);
        UI.Write(_bottomRow + 1, 2, string.Join("   ", bottomMenu));
        UI.Write(_bottomRow + 2, 2, "Tip: Prices scale with each purchase. Use arrows + Enter in the Shop.", ConsoleColor.DarkGray);
    }

    private void UpdateMoneyAndProd(bool force)
    {
        double money = _state.Money;
        double prod = _state.TotalProductionPerSecond(_catalog);

        if (force || !NearlyEqual(money, _lastMoneyDrawn))
        {
            // Fixed-width fields to overwrite prior text without clearing whole line
            UI.WriteField(_moneyRow, _moneyCol + 7, NumFmt.Format(money), 16);
            _lastMoneyDrawn = money;
        }
        if (force || !NearlyEqual(prod, _lastProdDrawn))
        {
            UI.WriteField(_prodRow, _prodCol + 8, NumFmt.Format(prod), 16);
            _lastProdDrawn = prod;
        }
    }

    private static bool NearlyEqual(double a, double b)
        => Math.Abs(a - b) <= 0.0001;

    private void DrawOwnedList(bool force)
    {
        // Clear owned area
        for (int r = _ownedStartRow; r < _bottomRow; r++)
            UI.ClearLine(r);

        var owned = _state.Items.Where(i => i.Quantity > 0).ToList();
        int row = _ownedStartRow;
        if (owned.Count == 0)
        {
            UI.Write(row, 4, "(None yet — visit the Shop with 'S')", ConsoleColor.DarkGray);
            return;
        }

        foreach (var st in owned)
        {
            var def = _catalog.ById(st.ItemId);

            // current multipliers
            double levelMult = Upgrades.MultiplierFor(st.UpgradeLevel);
            double prestigeMult = Prestige.ProdMultiplier(_state.PrestigeCredits);

            // per-unit and total production with multipliers
            double perUnit = def.BaseProductionPerSecond * levelMult * prestigeMult;
            double itemProd = perUnit * st.Quantity;

            UI.Write(row++, 4,
                $"- {def.Name} x{st.Quantity} (Lv{st.UpgradeLevel}) → {NumFmt.Format(itemProd)}/s");
        }

    }

    private void OpenShop()
    {
        int idx = 0;
        while (true)
        {
            UI.Clear();
            UI.WriteCentered(1, "Shop", ConsoleColor.Green, bold: true);
            UI.WriteCentered(2, $"Money: {NumFmt.Format(_state.Money)}", ConsoleColor.White);
            UI.WriteCentered(3, "↑/↓ select  •  Enter buy  •  B to go back ", ConsoleColor.DarkGray);

            var items = _catalog.All;
            int startRow = 6;
            for (int i = 0; i < items.Count; i++)
            {
                var def = items[i];
                var st = _state.GetItemState(def.Id);
                double price = Pricing.CurrentPrice(def, st.Quantity);
                bool selected = (i == idx);

                // 
                double levelMult = Upgrades.MultiplierFor(st.UpgradeLevel);
                double prestigeMult = Prestige.ProdMultiplier(_state.PrestigeCredits);
                double perUnitNow = def.BaseProductionPerSecond * levelMult * prestigeMult;

                string line = $"{def.Name,-16} (+{NumFmt.Format(perUnitNow, 2)}/s each)  Price: {NumFmt.Format(price)}  Owned: {st.Quantity}  Lv:{st.UpgradeLevel}";


                UI.Write(startRow + i, 4, line,
                    selected ? ConsoleColor.Black : ConsoleColor.White,
                    bg: selected ? ConsoleColor.Yellow : (ConsoleColor?)null);
            }

            UI.TickToasts();
            var key = Console.ReadKey(true).Key;
            if (key == ConsoleKey.B || key == ConsoleKey.Escape)
            {
                _ownedListDirty = true;
                _lastProdDrawn = double.NaN;
                _layoutDirty = true;
                return;
            }
            if (key == ConsoleKey.UpArrow) idx = (idx - 1 + items.Count) % items.Count;
            else if (key == ConsoleKey.DownArrow) idx = (idx + 1) % items.Count;
            else if (key == ConsoleKey.Enter)
            {
                var def = items[idx];
                var st = _state.GetItemState(def.Id);
                double price = Pricing.CurrentPrice(def, st.Quantity);
                if (_state.Money >= price)
                {
                    _state.Money -= price;
                    st.Quantity += 1;
                    UI.Toast($"Bought 1 {def.Name} for {NumFmt.Format(price)}.");
                    _ownedListDirty = true;
                    _lastProdDrawn = double.NaN;
                }
                else
                {
                    UI.Toast("Not enough money.");
                }
            }
        }
    }

    private void OpenUpgrades()
    {
        // Show only owned producers
        var ownedDefs = _catalog.All.Where(d => _state.GetItemState(d.Id).Quantity > 0).ToList();
        if (ownedDefs.Count == 0)
        {
            UI.Toast("You don't own any producers yet.");
            return;
        }
        int idx = 0;
        while (true)
        {
            UI.Clear();
            UI.WriteCentered(1, "Upgrades", ConsoleColor.Cyan, bold: true);
            UI.WriteCentered(2, $"Money: {NumFmt.Format(_state.Money)}", ConsoleColor.White);
            UI.WriteCentered(3, "↑/↓ select • Enter upgrade • B to go back", ConsoleColor.DarkGray);

            int startRow = 6;
            for (int i = 0; i < ownedDefs.Count; i++)
            {
                var def = ownedDefs[i];
                var st = _state.GetItemState(def.Id);
                int lvl = st.UpgradeLevel;
                double currMult = Upgrades.MultiplierFor(lvl);
                double nextMult = Upgrades.MultiplierFor(lvl + 1);
                double uPrice = Upgrades.UpgradePrice(def, lvl);
                bool selected = (i == idx);
                string line = $"{def.Name,-16} Lv:{lvl}  Mult: x{currMult:0.##} → x{nextMult:0.##}  Upgrade: {NumFmt.Format(uPrice)}";
                UI.Write(startRow + i, 4, line,
                    selected ? ConsoleColor.Black : ConsoleColor.White,
                    bg: selected ? ConsoleColor.Yellow : (ConsoleColor?)null);
            }

            UI.TickToasts();
            var key = Console.ReadKey(true).Key;
            if (key == ConsoleKey.B || key == ConsoleKey.Escape)
            {
                _ownedListDirty = true;
                _lastProdDrawn = double.NaN;
                _layoutDirty = true;
                return;
            }
            if (key == ConsoleKey.UpArrow) idx = (idx - 1 + ownedDefs.Count) % ownedDefs.Count;
            else if (key == ConsoleKey.DownArrow) idx = (idx + 1) % ownedDefs.Count;
            else if (key == ConsoleKey.Enter)
            {
                var def = ownedDefs[idx];
                var st = _state.GetItemState(def.Id);
                double price = Upgrades.UpgradePrice(def, st.UpgradeLevel);
                if (_state.Money >= price)
                {
                    _state.Money -= price;
                    st.UpgradeLevel += 1;
                    UI.Toast($"Upgraded {def.Name} to Lv{st.UpgradeLevel} for {NumFmt.Format(price)}.");
                    _ownedListDirty = true;
                    _lastProdDrawn = double.NaN;
                }
                else
                {
                    UI.Toast("Not enough money.");
                }
            }
        }
    }

    private void OpenPrestige()
    {
        using var __page = UI.Page();
        int potential = Prestige.CreditsEarnedNow(_state);
        UI.Clear();
        UI.WriteCentered(1, "Prestige", ConsoleColor.Yellow, bold: true);
        UI.WriteCentered(3, potential > 0 ? $"You can gain {potential} prestige credit(s)." : "Not enough progress for prestige yet.", ConsoleColor.White);
        UI.WriteCentered(5, $"Lifetime earnings: {NumFmt.Format(_state.LifetimeEarnings)}", ConsoleColor.Gray);
        UI.WriteCentered(7, "Enter = Confirm  •  Esc = Cancel", ConsoleColor.DarkGray);
        if (potential <= 0)
        {
            Console.ReadKey(true);
            // Ensure main HUD fully redraws when returning
            _ownedListDirty = true;
            _lastProdDrawn = double.NaN;
            _layoutDirty = true;
            return;
        }
        while (true)
        {
            UI.TickToasts();
            var key = Console.ReadKey(true).Key;
            if (key == ConsoleKey.Enter)
            {
                Prestige.ApplyReset(_state);
                SaveSystem.Save(_state, "saves/slot1.json");
                _ownedListDirty = true;
                _lastProdDrawn = double.NaN;
                _layoutDirty = true;
                UI.Toast("Prestiged! Production boosted.");
                return;
            }
            if (key == ConsoleKey.Escape)
            {
                _ownedListDirty = true;
                _lastProdDrawn = double.NaN;
                _layoutDirty = true;
                return;
            }
        }
    }
}
#endregion

#region Number Formatting
public static class NumFmt
{
    public static string Format(double value, int decimals = 2)
    {
        double abs = Math.Abs(value);
        if (abs < 1000) return value.ToString($"0.{new string('0', decimals)}");

        int tier = (int)Math.Floor(Math.Log10(abs) / 3.0); // thousands groups
        double scaled = value / Math.Pow(1000, tier);
        string suffix = tier switch
        {
            1 => "K",
            2 => "M",
            3 => "B",
            _ => AlphaSuffix(tier - 4) // 10^12 => tier=4 => index 0 => "aa"
        };
        return Trim(scaled, decimals) + suffix;
    }

    private static string Trim(double v, int decimals)
    {
        string s = v.ToString($"0.{new string('#', decimals)}");
        return s;
    }

    private static string AlphaSuffix(int index)
    {
        // Map 0 -> "aa", 1 -> "ab", ..., 25 -> "az", 26 -> "ba", etc.
        if (index < 0) return "";
        var chars = new List<char>();
        int n = index;
        do
        {
            int r = n % 26; // 0..25
            chars.Add((char)('a' + r));
            n /= 26;
        } while (n > 0);
        chars.Reverse();
        // Ensure at least 2 characters by left-padding with 'a'
        while (chars.Count < 2) chars.Insert(0, 'a');
        return new string(chars.ToArray());
    }
}
#endregion

#region Settings & Settings Menu
public sealed class Settings
{
    public int AutosaveSeconds { get; set; } = 30; // 0 = Off
}

public static class SettingsSystem
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static Settings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var s = JsonSerializer.Deserialize<Settings>(json, JsonOpts);
                if (s != null) return s;
            }
        }
        catch { }
        return new Settings();
    }

    public static void Save(Settings s, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(s, JsonOpts);
        File.WriteAllText(path, json);
    }
}

public static class SettingsMenu
{
    private static readonly int[] Choices = new[] { 0, 10, 30, 60, 120, 300 };

    public static void Run(Settings settings)
    {
        using var __page = UI.Page();
        int idx = Array.IndexOf(Choices, settings.AutosaveSeconds);
        if (idx < 0) idx = 2; // default to 30s

        while (true)
        {
            // Full redraw each loop so the displayed value always reflects current selection
            UI.Clear();
            UI.WriteCentered(1, "Settings", ConsoleColor.Cyan, bold: true);
            UI.WriteCentered(3, "Use ←/→ to change. Enter to confirm. Esc to return.", ConsoleColor.DarkGray);

            string val = Choices[idx] == 0 ? "Off" : Choices[idx] + "s";
            UI.WriteCentered(6, $"Autosave interval: {val}", ConsoleColor.White);

            // Read input
            UI.TickToasts();
            var key = Console.ReadKey(true).Key;
            if (key == ConsoleKey.LeftArrow) { idx = (idx - 1 + Choices.Length) % Choices.Length; continue; }
            if (key == ConsoleKey.RightArrow) { idx = (idx + 1) % Choices.Length; continue; }
            if (key == ConsoleKey.Enter)
            {
                settings.AutosaveSeconds = Choices[idx];
                SettingsSystem.Save(settings, "saves/settings.json");
                UI.Toast("Settings saved.");
                return;
            }
            if (key == ConsoleKey.Escape || key == ConsoleKey.Backspace)
            {
                return;
            }
        }
    }
}
#endregion

#region Prestige System
public static class Prestige
{
    public static int CreditsEarnedNow(GameState s)
        => PotentialCredits(s.LifetimeEarnings, s.PrestigeCreditsEarnedHistorical);

    public static int PotentialCredits(double lifetimeEarnings, int alreadyBanked, double baseThreshold = 1_000_000d)
    {
        if (lifetimeEarnings < baseThreshold) return 0;
        int totalCredits = (int)Math.Floor(Math.Log10(lifetimeEarnings) - Math.Log10(baseThreshold) + 1);
        int newCredits = totalCredits - alreadyBanked;
        return Math.Max(0, newCredits);
    }

    public static double ProdMultiplier(int credits) => 1.0 + 0.05 * credits;

    public static int ApplyReset(GameState s)
    {
        int earned = CreditsEarnedNow(s);
        if (earned <= 0) return 0;
        s.PrestigeCredits += earned;
        s.PrestigeCreditsEarnedHistorical += earned;
        s.Prestiges += 1;
        s.Money = 0;
        s.Items.Clear();
        return earned;
    }
}
#endregion

#region Upgrades (Per-Run Producer Leveling)
public static class Upgrades
{
    // Production multiplier per upgrade level (e.g., Lv1=1.5x, Lv2=2.25x, ...)
    public static double MultiplierFor(int level) => Math.Pow(1.5, level);

    // Upgrade price scales off the item's base cost and current level.
    public static double UpgradePrice(ItemDef def, int currentLevel)
        => def.BaseCost * 5 * Math.Pow(1.7, currentLevel);
}
#endregion

#region Domain Model & Economy
public record ItemDef(string Id, string Name, double BaseCost, double CostMultiplier, double BaseProductionPerSecond);

public sealed class ItemCatalog
{
    public List<ItemDef> All = new();

    public ItemDef ById(string id) => All.First(x => x.Id == id);

    public static ItemCatalog Default()
    {
        return new ItemCatalog
        {
            All = new List<ItemDef>
            {
                new("cursor", "Cursor",     10,   1.15, 0.1),
                new("worker", "Worker",     100,   1.15, 1),
                new("farm",   "Farm",      1_000, 1.15, 8),
                new("factory","Factory",  10_000, 1.15, 47),
                new("lab",     "Lab",     100_000, 1.15, 260),
            }
        };
    }
}

public static class Pricing
{
    // Simple exponential pricing: price(n) = base * multiplier^n
    public static double CurrentPrice(ItemDef def, int owned)
        => def.BaseCost * Math.Pow(def.CostMultiplier, owned);
}

public sealed class GameState
{
    public double Money { get; set; }
    public double LifetimeEarnings { get; set; } = 0; // cumulative earnings across runs
    public int Prestiges { get; set; } = 0;
    public int PrestigeCredits { get; set; } = 0;
    public int PrestigeCreditsEarnedHistorical { get; set; } = 0; // bookkeeping so we only award new credits

    public List<ItemState> Items { get; set; } = new();

    public static GameState New()
    {
        return new GameState
        {
            Money = 0,
            LifetimeEarnings = 0,
            Prestiges = 0,
            PrestigeCredits = 0,
            PrestigeCreditsEarnedHistorical = 0,
            Items = new List<ItemState>()
        };
    }

    public ItemState GetItemState(string id)
    {
        var found = Items.FirstOrDefault(i => i.ItemId == id);
        if (found == null)
        {
            found = new ItemState { ItemId = id, Quantity = 0 };
            Items.Add(found);
        }
        return found;
    }

    public double TotalProductionPerSecond(ItemCatalog catalog)
    {
        double total = 0;
        foreach (var st in Items)
        {
            if (st.Quantity <= 0) continue;
            var def = catalog.ById(st.ItemId);
            total += def.BaseProductionPerSecond * st.Quantity * Upgrades.MultiplierFor(st.UpgradeLevel);
        }
        // Apply permanent prestige multiplier
        total *= Prestige.ProdMultiplier(PrestigeCredits);
        return total;
    }
}

public sealed class ItemState
{
    public string ItemId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int UpgradeLevel { get; set; } = 0; // per-item leveling within a run
}
#endregion

#region Save System
public static class SaveSystem
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Save(GameState state, string path)
    {
        var json = JsonSerializer.Serialize(state, JsonOpts);
        File.WriteAllText(path, json);
    }

    public static GameState? TryLoad(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            string json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<GameState>(json, JsonOpts);
            return state;
        }
        catch
        {
            return null;
        }
    }
}
#endregion

#region UI Helpers
public static class UI
{
    private sealed class PageGuard : IDisposable
    {
        public PageGuard()
        {
            try { Console.CursorVisible = false; } catch { }
            Console.ResetColor();
            Clear();
            _toastUntilUtc = null;
        }
        public void Dispose()
        {
            // Do not clear on exit; let the caller/layout redraw next frame
            Console.ResetColor();
        }
    }

    public static IDisposable Page() => new PageGuard();

    private static DateTime? _toastUntilUtc;
    private static int _toastRow;

    public static void Clear()
    {
        try { Console.Clear(); } catch { /* ignore in some terminals */ }
    }

    public static void Cleanup()
    {
        Console.ResetColor();
        Console.CursorVisible = true;
        Console.Clear();
        _toastUntilUtc = null;
    }

    public static void Write(int row, int col, string text, ConsoleColor? fg = null, ConsoleColor? bg = null)
    {
        SafeSetPos(row, col);
        if (bg.HasValue) Console.BackgroundColor = bg.Value;
        if (fg.HasValue) Console.ForegroundColor = fg.Value;
        Console.Write(text);
        Console.ResetColor();
    }

    public static void WriteField(int row, int col, string text, int maxWidth)
    {
        string s = text.Length > maxWidth ? text[..maxWidth] : text.PadRight(maxWidth);
        Write(row, col, s);
    }

    public static void WriteCentered(int row, string text, ConsoleColor? fg = null, ConsoleColor? bg = null, bool bold = false)
    {
        int width = Math.Max(1, Console.WindowWidth);
        int col = Math.Max(0, (width - text.Length) / 2);
        if (bold) text = $"{text}";
        Write(row, col, text, fg, bg);
    }

    public static void Toast(string message, int milliseconds = 4000)
    {
        _toastRow = Console.WindowHeight - 1;
        ClearLine(_toastRow);
        Write(_toastRow, 2, message, ConsoleColor.Black, ConsoleColor.Gray);
        _toastUntilUtc = DateTime.UtcNow.AddMilliseconds(milliseconds);
    }

    public static void TickToasts()
    {
        if (_toastUntilUtc.HasValue && DateTime.UtcNow >= _toastUntilUtc.Value)
        {
            ClearLine(_toastRow);
            _toastUntilUtc = null;
        }
    }

    public static void ClearLine(int row)
    {
        int width = Console.WindowWidth;
        Write(row, 0, new string(' ', Math.Max(0, width - 1)));
    }

    private static void SafeSetPos(int row, int col)
    {
        row = Math.Clamp(row, 0, Math.Max(0, Console.WindowHeight - 1));
        col = Math.Clamp(col, 0, Math.Max(0, Console.WindowWidth - 1));
        try { Console.SetCursorPosition(col, row); } catch { /* terminal might be too small */ }
    }
}
#endregion
