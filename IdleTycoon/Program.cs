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
        var settings = SettingsSystem.Load("saves/settings.json");

        while (true)
        {
            var choice = StartMenu.Run();
            switch (choice)
            {
                case StartMenu.Option.NewGame:
                    var newState = GameState.New();
                    new Game(newState, settings).Run();
                    break;
                case StartMenu.Option.Continue:
                    var loaded = SaveSystem.TryLoad("saves/slot1.json");
                    if (loaded != null)
                        new Game(loaded, settings).Run();
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

    private ItemCatalog _catalog = new();

    // Cached layout positions so we can do partial redraws
    private int _width, _height;
    private int _moneyRow = 1, _moneyCol = 2;
    private int _prodRow = 1, _prodCol;
    private int _ownedHeaderRow = 6, _ownedStartRow = 8;
    private int _bottomRow;

    // Dirty flags
    private bool _layoutDirty = true;      // window resized or first draw
    private bool _ownedListDirty = true;   // purchases/level change changed list

    // Last displayed values to avoid redundant writes
    private double _lastMoneyDrawn = double.NaN;
    private double _lastProdDrawn = double.NaN;

    public Game(GameState state, Settings settings)
    {
        _state = state;
        _settings = settings;
        LoadCatalogFromCurrentLevel();
    }

    private void LoadCatalogFromCurrentLevel()
    {
        var level = Campaign.Current(_state);
        _catalog = new ItemCatalog { All = level.Producers };
        _ownedListDirty = true;
        _layoutDirty = true;
    }

    public void Run()
    {
        var watch = Stopwatch.StartNew();
        double last = watch.Elapsed.TotalSeconds;
        bool running = true;

        // Initial layout sizing
        _width = Console.WindowWidth;
        _height = Console.WindowHeight;

        DrawStaticHUD();
        DrawOwnedList(force: true);
        UpdateMoneyAndProd(force: true);

        double autosaveTimer = 0;
        while (running)
        {
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

            // Level completion check
            var lvl = Campaign.Current(_state);
            if (_state.Money >= lvl.GoalMoney)
            {
                OpenLevelCompleteDialog(lvl);
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
                DrawStaticHUD();
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

    private void DrawStaticHUD()
    {
        UI.Clear();
        _prodCol = Math.Max(0, _width / 2);

        // Header bars
        UI.Write(0, 0, new string('─', Math.Max(0, _width - 1)), ConsoleColor.DarkGray);
        UI.Write(_moneyRow, 0, new string(' ', Math.Max(0, _width - 1))); // clear line for fields
        UI.Write(2, 0, new string('─', Math.Max(0, _width - 1)), ConsoleColor.DarkGray);

        // Level & Goal
        var level = Campaign.Current(_state);
        string lvlLine = $"Level: {level.Name}   Goal: {NumFmt.Format(level.GoalMoney)}";
        UI.Write(3, 2, lvlLine, ConsoleColor.White);
        DrawProgressBar(4, 2, _width - 4, _state.Money, level.GoalMoney);

        // Static labels (values filled by UpdateMoneyAndProd)
        UI.Write(_moneyRow, _moneyCol, "Money: ");
        UI.Write(_prodRow, _prodCol, "Prod/s: ");

        // Owned header & prestige info
        UI.Write(_ownedHeaderRow - 1, 2, $"Prestige: {_state.PrestigeCredits} (x{Prestige.ProdMultiplier(_state.PrestigeCredits):0.00})", ConsoleColor.DarkYellow);
        UI.Write(_ownedHeaderRow, 2, "Owned Producers:", ConsoleColor.Cyan);

        // Column headers for owned list
        // Name(20) | Qty(6 R) | Lv(4 R) | Unit/s(14 R) | Total/s(14 R)
        UI.Columns(_ownedHeaderRow + 1, 4,
            ("Name", 20, false),
            ("Qty", 6, true),
            ("Lv", 4, true),
            ("Unit/s", 14, true),
            ("Total/s", 14, true)
        );
        UI.Write(_ownedHeaderRow + 2, 4, new string('─', Math.Min(Console.WindowWidth - 8, 60)), ConsoleColor.DarkGray);

        // Bottom menu & divider
        _bottomRow = _height - 4;
        if (_bottomRow < _ownedStartRow + 2) _bottomRow = _ownedStartRow + 2;
        UI.Write(_bottomRow, 0, new string('─', Math.Max(0, _width - 1)), ConsoleColor.DarkGray);
        var bottomMenu = new[] {
            "[S]hop", "[U] Upgrades", "[F5] Save", "[P] Prestige", "[Esc] Exit to Menu", "[Space] Collect (if any click mechanic)",
            _settings.AutosaveSeconds > 0 ? $"Autosave: {_settings.AutosaveSeconds}s" : "Autosave: Off"
        };
        UI.Write(_bottomRow + 1, 2, string.Join("   ", bottomMenu));
        UI.Write(_bottomRow + 2, 2, "Tip: Prices scale with each purchase. Use arrows + Enter in the Shop.", ConsoleColor.DarkGray);
    }

    private void DrawProgressBar(int row, int col, int width, double value, double goal)
    {
        width = Math.Max(20, width);
        double pct = Math.Clamp(goal <= 0 ? 0 : value / goal, 0, 1);
        int inner = Math.Max(10, width - 12);
        int filled = (int)Math.Round(inner * pct);
        string bar = "[" + new string('█', filled) + new string('░', Math.Max(0, inner - filled)) + $"] {pct * 100:0.0}%";
        UI.Write(row, col, bar, ConsoleColor.Gray);
    }

    private void UpdateMoneyAndProd(bool force)
    {
        double money = _state.Money;
        double prod = _state.TotalProductionPerSecond(_catalog);

        if (force || !NearlyEqual(money, _lastMoneyDrawn))
        {
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

            UI.Columns(row++, 4,
                (def.Name, 20, false),
                (st.Quantity.ToString(), 6, true),
                ($"{st.UpgradeLevel}", 4, true),
                ($"{NumFmt.Format(perUnit)}", 14, true),
                ($"{NumFmt.Format(itemProd)}", 14, true)
            );
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

            // Headers: Name(20) | Unit/s(14 R) | Price(16 R) | Owned(6 R) | Lv(4 R)
            int headerRow = 5;
            UI.Columns(headerRow, 4,
                ("Name", 20, false),
                ("Unit/s", 14, true),
                ("Price", 16, true),
                ("Owned", 6, true),
                ("Lv", 4, true));
            UI.Write(headerRow + 1, 4, new string('─', Math.Min(Console.WindowWidth - 8, 70)), ConsoleColor.DarkGray);

            var items = _catalog.All;
            int startRow = headerRow + 3;
            for (int i = 0; i < items.Count; i++)
            {
                var def = items[i];
                var st = _state.GetItemState(def.Id);
                double price = Pricing.CurrentPrice(def, st.Quantity);

                double levelMult = Upgrades.MultiplierFor(st.UpgradeLevel);
                double prestigeMult = Prestige.ProdMultiplier(_state.PrestigeCredits);
                double perUnitNow = def.BaseProductionPerSecond * levelMult * prestigeMult;

                bool selected = (i == idx);
                var fg = selected ? ConsoleColor.Black : ConsoleColor.White;
                var bg = selected ? ConsoleColor.Yellow : (ConsoleColor?)null;

                UI.Columns(startRow + i, 4, fg, bg,
                    (def.Name, 20, false),
                    (NumFmt.Format(perUnitNow, 2), 14, true),
                    (NumFmt.Format(price), 16, true),
                    (st.Quantity.ToString(), 6, true),
                    (st.UpgradeLevel.ToString(), 4, true)
                );
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

            // Headers: Name(20) | Lv(4 R) | Mult(18) | Upgrade(16 R)
            int headerRow = 5;
            UI.Columns(headerRow, 4,
                ("Name", 20, false),
                ("Lv", 4, true),
                ("Mult (cur→next)", 18, false),
                ("Upgrade", 16, true));
            UI.Write(headerRow + 1, 4, new string('─', Math.Min(Console.WindowWidth - 8, 70)), ConsoleColor.DarkGray);

            int startRow = headerRow + 3;
            for (int i = 0; i < ownedDefs.Count; i++)
            {
                var def = ownedDefs[i];
                var st = _state.GetItemState(def.Id);
                int lvl = st.UpgradeLevel;
                double currMult = Upgrades.MultiplierFor(lvl);
                double nextMult = Upgrades.MultiplierFor(lvl + 1);
                double uPrice = Upgrades.UpgradePrice(def, lvl);

                bool selected = (i == idx);
                var fg = selected ? ConsoleColor.Black : ConsoleColor.White;
                var bg = selected ? ConsoleColor.Yellow : (ConsoleColor?)null;

                UI.Columns(startRow + i, 4, fg, bg,
                    (def.Name, 20, false),
                    (lvl.ToString(), 4, true),
                    ($"x{currMult:0.##}→x{nextMult:0.##}", 18, false),
                    (NumFmt.Format(uPrice), 16, true)
                );
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

        // Two-column summary: Field(16) | Value
        int row = 4;
        UI.Columns(row++, 6, ("Field", 16, false), ("Value", 32, false));
        UI.Write(row++, 6, new string('─', Math.Min(Console.WindowWidth - 12, 48)), ConsoleColor.DarkGray);
        UI.Columns(row++, 6, ("Lifetime", 16, false), (NumFmt.Format(_state.LifetimeEarnings), 32, false));
        UI.Columns(row++, 6, ("Credits", 16, false), (_state.PrestigeCredits.ToString(), 32, false));
        UI.Columns(row++, 6, ("Now available", 16, false), (potential.ToString(), 32, false));
        UI.WriteCentered(row + 1, "Enter = Confirm  •  Esc = Cancel", ConsoleColor.DarkGray);

        if (potential <= 0)
        {
            Console.ReadKey(true);
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

    private void OpenLevelCompleteDialog(LevelDef level)
    {
        using var __page = UI.Page();
        UI.WriteCentered(1, "Level Complete!", ConsoleColor.Green, bold: true);
        UI.WriteCentered(3, $"You reached the goal for '{level.Name}'.", ConsoleColor.White);

        var next = Campaign.Next(_state);
        if (next != null)
        {
            UI.WriteCentered(5, $"Next: {next.Name}", ConsoleColor.Cyan);
            UI.WriteCentered(7, "[Enter] Advance   •   [C] Continue here   •   [Esc] Cancel", ConsoleColor.DarkGray);
        }
        else
        {
            UI.WriteCentered(5, "This is the last level in the campaign (for now).", ConsoleColor.Cyan);
            UI.WriteCentered(7, "[C] Continue here   •   [Esc] Close", ConsoleColor.DarkGray);
        }

        while (true)
        {
            UI.TickToasts();
            var key = Console.ReadKey(true).Key;

            if (key == ConsoleKey.Enter && next != null)
            {
                // Advance: unlock & switch, reset run state
                _state.UnlockedLevels.Add(next.Id);
                _state.CurrentLevelId = next.Id;
                _state.Money = 0;
                _state.Items.Clear();
                LoadCatalogFromCurrentLevel();
                SaveSystem.Save(_state, "saves/slot1.json");
                UI.Toast($"Advanced to {next.Name}!");
                return;
            }
            if (key == ConsoleKey.C || key == ConsoleKey.Escape)
            {
                _layoutDirty = true;
                _ownedListDirty = true;
                _lastProdDrawn = double.NaN;
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
            UI.Clear();
            UI.WriteCentered(1, "Settings", ConsoleColor.Cyan, bold: true);
            UI.WriteCentered(3, "Use ←/→ to change. Enter to confirm. Esc to return.", ConsoleColor.DarkGray);

            string val = Choices[idx] == 0 ? "Off" : Choices[idx] + "s";
            UI.WriteCentered(6, $"Autosave interval: {val}", ConsoleColor.White);

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

#region Levels & Campaign
public record LevelDef(string Id, string Name, double GoalMoney, List<ItemDef> Producers);

public static class Campaign
{
    public static readonly List<LevelDef> Levels = new()
    {
        new("lvl1", "Desert Outskirts", 1_000_000, new()
        {
            new("vaporator","Moisture Vaporator",10,1.15,0.10),
            new("junkdroid","Salvage Droid",60,1.15,0.70),
            new("landspeeder","Landspeeder Runs",600,1.15,6),
            new("cantina","Cantina Stalls",6_000,1.15,45),
            new("freighter","Small Freighter",60_000,1.15,300),
        }),
        new("lvl2", "Spaceport Fringe", 50_000_000, new()
        {
            new("droidshop","Droid Workshop",200,1.15,1.8),
            new("hangar","Hangar Bay",2_000,1.15,14),
            new("market","Bazaar Network",20_000,1.15,95),
            new("courier","Courier Routes",200_000,1.15,620),
            new("guild","Guild Contracts",2_000_000,1.15,4_000),
        }),
        new("lvl3", "Rebel Outpost", 2_000_000_000, new()
        {
            new("lookout","Lookout Posts",1_000,1.15,3.5),
            new("comms","Comms Relay",10_000,1.15,26),
            new("cells","Rebel Cells",100_000,1.15,180),
            new("supply","Supply Lines",1_000_000,1.15,1_150),
            new("wing","Starfighter Wing",10_000_000,1.15,7_200),
        }),
        new("lvl4", "Icy Holdfast", 80_000_000_000, new()
        {
            new("patrol","Perimeter Patrols",6_000,1.15,20),
            new("shield","Field Generators",60_000,1.15,140),
            new("depot","Supply Depot",600_000,1.15,980),
            new("hanger","Heavy Hangars",6_000_000,1.15,6_600),
            new("convoy","Convoy Command",60_000_000,1.15,45_000),
        }),
        new("lvl5", "Cloud Mines", 3_000_000_000_000, new()
        {
            new("lift","Gas Lifters",30_000,1.15,70),
            new("refine","Refinery Lines",300_000,1.15,500),
            new("trade","Trade Consortium",3_000_000,1.15,3_400),
            new("harbor","Sky Harbor",30_000_000,1.15,22_000),
            new("charter","Charter Fleets",300_000_000,1.15,150_000),
        }),
        new("lvl6", "Capital Shipyards", 120_000_000_000_000, new()
        {
            new("drydock","Drydock Crews",150_000,1.15,300),
            new("frames","Hull Frames",1_500_000,1.15,2_100),
            new("yards","Orbital Yards",15_000_000,1.15,14_000),
            new("fleets","Fleet Logistics",150_000_000,1.15,95_000),
            new("capital","Capital Lines",1_500_000_000,1.15,650_000),
        }),
    };

    public static LevelDef Current(GameState s)
        => Levels.First(l => l.Id == s.CurrentLevelId);

    public static LevelDef? Next(GameState s)
    {
        var i = Levels.FindIndex(l => l.Id == s.CurrentLevelId);
        return (i >= 0 && i < Levels.Count - 1) ? Levels[i + 1] : null;
    }
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

    public string CurrentLevelId { get; set; } = "lvl1";
    public HashSet<string> UnlockedLevels { get; set; } = new() { "lvl1" };

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
            CurrentLevelId = "lvl1",
            UnlockedLevels = new HashSet<string> { "lvl1" },
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

    // --- New: simple column renderer -------------------------------------------------
    // cols: (text, width, rightAlign)
    public static void Columns(int row, int startCol, params (string text, int width, bool right)[] cols)
        => Columns(row, startCol, null, null, cols);

    public static void Columns(int row, int startCol, ConsoleColor? fg, ConsoleColor? bg,
        params (string text, int width, bool right)[] cols)
    {
        int col = startCol;
        foreach (var c in cols)
        {
            string cell = c.text ?? "";
            // truncate
            if (cell.Length > c.width) cell = cell[..c.width];
            // pad
            cell = c.right ? cell.PadLeft(c.width) : cell.PadRight(c.width);
            Write(row, col, cell, fg, bg);
            col += c.width + 2; // 2 spaces gap
        }
    }
    // -------------------------------------------------------------------------------

    private static void SafeSetPos(int row, int col)
    {
        row = Math.Clamp(row, 0, Math.Max(0, Console.WindowHeight - 1));
        col = Math.Clamp(col, 0, Math.Max(0, Console.WindowWidth - 1));
        try { Console.SetCursorPosition(col, row); } catch { /* terminal might be too small */ }
    }
}
#endregion
