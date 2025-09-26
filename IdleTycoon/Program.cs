using IdleTycoon.Core.GameState;
using IdleTycoon.Gameplay;
using IdleTycoon.Systems;
using IdleTycoon.Systems.Settings;
using IdleTycoon.Ui;
using IdleTycoon.Ui.ConsoleUI;
using IdleTycoon.Ui.Pages;

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
