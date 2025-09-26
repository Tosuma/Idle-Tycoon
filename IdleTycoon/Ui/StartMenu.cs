using IdleTycoon.Ui.ConsoleUI;

namespace IdleTycoon.Ui;

public static class StartMenu
{
    public enum Option { Continue, NewGame, Settings, Exit }

    public static Option Run()
    {
        using var __page = new PageGuard();
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
                bool selected = i == idx;
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
