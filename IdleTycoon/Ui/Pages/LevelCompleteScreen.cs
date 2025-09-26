using IdleTycoon.Core.GameState;
using IdleTycoon.Gameplay.CampaignDesign;
using IdleTycoon.Systems;
using IdleTycoon.Ui.ConsoleUI;
using System;

namespace IdleTycoon.Ui.Pages;

public static class LevelCompleteScreen
{
    /// <summary>
    /// Shows completion dialog. Returns true if advanced to the next level (state mutated).
    /// </summary>
    public static bool Run(GameState state, LevelDef level)
    {
        using var __ = new PageGuard();

        UI.WriteCentered(1, "Level Complete!", ConsoleColor.Green, bold: true);
        UI.WriteCentered(3, $"You reached the goal for '{level.Name}'.", ConsoleColor.White);

        var next = Campaign.Next(state);
        if (next is not null)
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

            if (key is ConsoleKey.Enter && next is not null)
            {
                // Advance: unlock & switch, reset run state
                state.UnlockedLevels.Add(next.Id);
                state.CurrentLevelId = next.Id;
                state.Money = 0;
                state.Items.Clear();
                SaveSystem.Save(state, "saves/slot1.json");
                UI.Toast($"Advanced to {next.Name}!");
                return true;
            }
            if (key is ConsoleKey.C or ConsoleKey.Escape)
                return false;
        }
    }
}
