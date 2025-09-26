using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IdleTycoon.Ui.ConsoleUI;
/// <summary>
/// Simple scope guard for console "pages".
/// Clears on enter, resets colors, hides cursor; resets colors on exit.
/// Use with: using var _ = new PageGuard();
/// </summary>
public sealed class PageGuard : IDisposable
{
    public PageGuard()
    {
        try { Console.CursorVisible = false; } catch { }
        Console.ResetColor();
        try { Console.Clear(); } catch { }
    }

    public void Dispose()
    {
        Console.ResetColor();
        // Leave the screen drawn by the page; caller decides next draw.
    }
}
