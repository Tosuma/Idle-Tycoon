using IdleTycoon.Core.Economy;
using IdleTycoon.Core.GameState;
using IdleTycoon.Core.Numbers;
using IdleTycoon.Gameplay.CampaignDesign;
using IdleTycoon.Systems;
using IdleTycoon.Systems.Settings;
using IdleTycoon.Ui.ConsoleUI;
using IdleTycoon.Ui.Pages;
using System.Diagnostics;
using System.Reflection.Emit;

namespace IdleTycoon.Gameplay;

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
                DrawProgressBar(4, 2, _width - 4, _state.Money, Campaign.Current(_state).GoalMoney);

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
                        ShopScreen.Run(_state, _catalog);
                        _layoutDirty = true; // redraw entire layout to avoid artifacts
                        _ownedListDirty = true;
                        _lastProdDrawn = double.NaN;
                        break;
                    case ConsoleKey.U:
                        if (UpgradesScreen.Run(_state, _catalog))
                        {
                            _layoutDirty = true; // redraw entire layout to avoid artifacts
                            _ownedListDirty = true;
                            _lastProdDrawn = double.NaN;
                        }
                        break;
                    case ConsoleKey.F5:
                        SaveSystem.Save(_state, "saves/slot1.json");
                        UI.Toast("Game saved.");
                        break;
                    case ConsoleKey.Spacebar:
                        _state.Money += 1.0; _state.LifetimeEarnings += 1.0;
                        break;
                    case ConsoleKey.P:
                        PrestigeScreen.Run(_state);
                        _layoutDirty = true; // redraw entire layout to avoid artifacts
                        _ownedListDirty = true;
                        _lastProdDrawn = double.NaN;
                        break;
                    case ConsoleKey.L:
                        // Level completion check
                        var lvl = Campaign.Current(_state);
                        if (_state.Money >= lvl.GoalMoney)
                        {
                            if (LevelCompleteScreen.Run(_state, lvl)) // level advanced
                            {
                                LoadCatalogFromCurrentLevel();
                                last = watch.Elapsed.TotalSeconds; // reset tick timer to avoid large dt spike
                            }
                            else // not level advanced
                            {
                                _layoutDirty = true; // redraw entire layout to avoid artifacts
                                _ownedListDirty = true;
                                _lastProdDrawn = double.NaN;
                            }
                        }
                        else
                        {
                            UI.Toast("Level goal not yet reached.");
                        }
                        break;
                    case ConsoleKey.Escape:
                        running = ConfirmExit();
                        break;
                }
            }

            UI.TickToasts();
            Thread.Sleep(33); // ~30 FPS polling
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
            ("Unit/s", 8, true),
            ("Total/s", 8, true)
        );
        UI.Write(_ownedHeaderRow + 2, 4, new string('─', Math.Min(Console.WindowWidth - 8, 60)), ConsoleColor.DarkGray);

        // Bottom menu & divider
        _bottomRow = _height - 4;
        if (_bottomRow < _ownedStartRow + 2) _bottomRow = _ownedStartRow + 2;
        UI.Write(_bottomRow, 0, new string('─', Math.Max(0, _width - 1)), ConsoleColor.DarkGray);
        var bottomMenu = new[] {
            "[Space] Collect",
            "[S] Shop",
            "[U] Upgrades",
            "[L] Level up",
            "[P] Prestige",
            "[F5] Save",
            "[Esc] Exit to Menu",
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
                ($"{NumFmt.Format(perUnit)}", 8, true),
                ($"{NumFmt.Format(itemProd)}", 8, true)
            );
        }
    }

}
