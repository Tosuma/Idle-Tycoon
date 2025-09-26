
using IdleTycoon.Systems.Settings;
using IdleTycoon.Ui.ConsoleUI;

namespace IdleTycoon.Ui.Pages;
public static class SettingsMenu
{
    private static readonly int[] Choices = new[] { 0, 10, 30, 60, 120, 300 };

    public static void Run(Settings settings)
    {
        using var __page = new PageGuard();
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