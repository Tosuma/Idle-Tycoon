using IdleTycoon.Core.Economy;
using IdleTycoon.Core.GameState;
using IdleTycoon.Core.Numbers;
using IdleTycoon.Gameplay;
using IdleTycoon.Ui.ConsoleUI;

namespace IdleTycoon.Ui.Pages;

public static class UpgradesScreen
{
    /// <summary>
    /// Upgrade producers you already own. Returns when user backs out.
    /// </summary>
    public static bool Run(GameState state, ItemCatalog catalog, int windowPadding = 4)
    {
        var ownedDefs = catalog.All.Where(d => state.GetItemState(d.Id).Quantity > 0).ToList();
        if (ownedDefs.Count == 0)
        {
            UI.Toast("You don't own any producers yet.");
            return false;
        }

        using var __ = new PageGuard();
        int idx = 0;

        while (true)
        {
            UI.Clear();
            UI.WriteCentered(1, "Upgrades", ConsoleColor.Cyan, bold: true);
            UI.WriteCentered(2, $"Money: {NumFmt.Format(state.Money)}", ConsoleColor.White);
            UI.WriteCentered(3, "↑/↓ select • Enter upgrade • B to go back", ConsoleColor.DarkGray);

            // Headers: Name(20) | Lv(4 R) | Mult(18) | Upgrade(16 R)
            int headerRow = 5;
            UI.Columns(headerRow, windowPadding,
                ("Name", 20, false),
                ("Lv", 4, true),
                ("Mult (cur→next)", 18, false),
                ("Upgrade", 16, true));
            UI.Write(headerRow + 1, windowPadding, new string('─', Math.Min(Console.WindowWidth - (windowPadding * 2), 70)), ConsoleColor.DarkGray);

            int startRow = headerRow + 3;
            for (int i = 0; i < ownedDefs.Count; i++)
            {
                var def = ownedDefs[i];
                var st = state.GetItemState(def.Id);
                int lvl = st.UpgradeLevel;
                double currMult = Upgrades.MultiplierFor(lvl);
                double nextMult = Upgrades.MultiplierFor(lvl + 1);
                double uPrice = Upgrades.UpgradePrice(def, lvl);

                bool selected = (i == idx);
                var fg = selected ? ConsoleColor.Black : ConsoleColor.White;
                var bg = selected ? ConsoleColor.Yellow : (ConsoleColor?)null;

                UI.Columns(startRow + i, windowPadding, fg, bg, TextStyle.None,
                    (def.Name, 20, false),
                    (lvl.ToString(), 4, true),
                    ($"x{currMult:0.##}→x{nextMult:0.##}", 18, false),
                    (NumFmt.Format(uPrice), 16, true)
                );
            }

            UI.TickToasts();
            var key = Console.ReadKey(true).Key;
            if (key is ConsoleKey.U or ConsoleKey.B or ConsoleKey.Escape)
                return true;
            if (key is ConsoleKey.UpArrow)
                idx = (idx - 1 + ownedDefs.Count) % ownedDefs.Count;
            else if (key is ConsoleKey.DownArrow)
                idx = (idx + 1) % ownedDefs.Count;
            else if (key is ConsoleKey.Enter)
            {
                var def = ownedDefs[idx];
                var st = state.GetItemState(def.Id);
                double price = Upgrades.UpgradePrice(def, st.UpgradeLevel);
                if (state.Money >= price)
                {
                    state.Money -= price;
                    st.UpgradeLevel += 1;
                    UI.Toast($"Upgraded {def.Name} to Lv{st.UpgradeLevel} for {NumFmt.Format(price)}.");
                }
                else
                {
                    UI.Toast("Not enough money.");
                }
            }
        }
    }
}
