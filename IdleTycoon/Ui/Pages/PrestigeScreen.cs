using IdleTycoon.Core.GameState;
using IdleTycoon.Core.Numbers;
using IdleTycoon.Gameplay;
using IdleTycoon.Systems;
using IdleTycoon.Ui.ConsoleUI;
using System;

namespace IdleTycoon.Ui.Pages;

public static class PrestigeScreen
{
    /// <summary>
    /// Shows prestige info; returns true if the player prestiged (state was reset).
    /// </summary>
    public static void Run(GameState state)
    {
        using var __ = new PageGuard();

        var target = Prestige.NextNewCreditThreshold(state.LifetimeEarnings, state.PrestigeCreditsEarnedHistorical);
        var remaining = Math.Max(0, target - state.LifetimeEarnings);
        int potential = Prestige.CreditsEarnedNow(state);
        UI.Clear();
        UI.WriteCentered(1, "Prestige", ConsoleColor.Yellow, bold: true);

        // Two-column summary: Field(16) | Value
        int row = 4;
        UI.Write(row++, 6, new string('─', Math.Min(Console.WindowWidth - 12, 48)), ConsoleColor.DarkGray);
        UI.Columns(row++, 6, ("Lifetime", 16, false), (NumFmt.Format(state.LifetimeEarnings), 32, false));
        UI.Columns(row++, 6, ("Credits", 16, false), (state.PrestigeCredits.ToString(), 32, false));
        UI.Columns(row++, 6, ("Now available", 16, false), (potential.ToString(), 32, false));

        UI.Columns(row++, 6, ("Next target", 16, false), (NumFmt.Format(target), 32, false));
        UI.Columns(row++, 6, ConsoleColor.DarkGray, ("Remaining", 16, false), (NumFmt.Format(remaining), 32, false));

        UI.WriteCentered(row + 1, "Enter = Confirm  •  Esc = Cancel", ConsoleColor.DarkGray);

        if (potential <= 0)
        {
            Console.ReadKey(true);
            return;
        }

        while (true)
        {
            UI.TickToasts();
            var key = Console.ReadKey(true).Key;
            if (key is ConsoleKey.P or ConsoleKey.Enter)
            {
                Prestige.ApplyReset(state);
                SaveSystem.Save(state, "saves/slot1.json");
                UI.Toast("Prestiged! Production boosted.");
                return;
            }
            if (key is ConsoleKey.Escape)
            {
                return;
            }
        }
    }
}
