using IdleTycoon.Core.Economy;
using IdleTycoon.Core.GameState;
using IdleTycoon.Core.Numbers;
using IdleTycoon.Gameplay;
using IdleTycoon.Ui.ConsoleUI;
using System;
using System.Linq;

namespace IdleTycoon.Ui.Pages;

public static class ShopScreen
{
    /// <summary>
    /// Blocking shop loop. Returns when the user backs out.
    /// </summary>
    public static void Run(GameState state, ItemCatalog catalog, int windowPadding = 4)
    {
        using var __ = new PageGuard();

        int idx = 0;

        while (true)
        {
            UI.Clear();
            UI.WriteCentered(1, "Shop", ConsoleColor.Green, bold: true);
            UI.WriteCentered(2, $"Money: {NumFmt.Format(state.Money)}", ConsoleColor.White);
            UI.WriteCentered(3, "↑/↓ select  •  Enter buy  •  B to go back ", ConsoleColor.DarkGray);

            // Headers: Name(20) | Unit/s(14 R) | Price(16 R) | Owned(6 R) | Lv(4 R)
            int headerRow = 5;
            UI.Columns(headerRow, windowPadding,
                ("Name", 20, false),
                ("Unit/s", 14, true),
                ("Price", 16, true),
                ("Owned", 6, true),
                ("Lv", 4, true));
            UI.Write(headerRow + 1, windowPadding, new string('─', Math.Min(Console.WindowWidth - (windowPadding * 2), 70)), ConsoleColor.DarkGray);

            var items = catalog.All;
            int startRow = headerRow + 3;
            for (int i = 0; i < items.Count; i++)
            {
                var def = items[i];
                var st = state.GetItemState(def.Id);
                double price = Pricing.CurrentPrice(def, st.Quantity);

                double levelMult = Upgrades.MultiplierFor(st.UpgradeLevel);
                double prestigeMult = Prestige.ProdMultiplier(state.PrestigeCredits);
                double perUnitNow = def.BaseProductionPerSecond * levelMult * prestigeMult;

                bool selected = (i == idx);
                var fg = selected ? ConsoleColor.Black : ConsoleColor.White;
                var bg = selected ? ConsoleColor.Yellow : (ConsoleColor?)null;

                UI.Columns(startRow + i, windowPadding, fg, bg, TextStyle.None,
                    (def.Name, 20, false),
                    (NumFmt.Format(perUnitNow, 2), 14, true),
                    (NumFmt.Format(price), 16, true),
                    (st.Quantity.ToString(), 6, true),
                    (st.UpgradeLevel.ToString(), 4, true)
                );
            }

            UI.TickToasts();
            var key = Console.ReadKey(true).Key;
            if (key is ConsoleKey.S or ConsoleKey.B or ConsoleKey.Escape)
                return;
            if (key is ConsoleKey.UpArrow)
                idx = (idx - 1 + items.Count) % items.Count;
            else if (key is ConsoleKey.DownArrow)
                idx = (idx + 1) % items.Count;
            else if (key is ConsoleKey.Enter)
            {
                var def = items[idx];
                var st = state.GetItemState(def.Id);
                double price = Pricing.CurrentPrice(def, st.Quantity);
                if (state.Money >= price)
                {
                    state.Money -= price;
                    st.Quantity += 1;
                    UI.Toast($"Bought 1 {def.Name} for {NumFmt.Format(price)}.");
                }
                else
                {
                    UI.Toast("Not enough money.");
                }
            }
        }
    }
}
