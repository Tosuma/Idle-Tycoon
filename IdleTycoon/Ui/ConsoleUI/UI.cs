using System.Runtime.InteropServices;

namespace IdleTycoon.Ui.ConsoleUI;
public static class UI
{
    private static DateTime? _toastUntilUtc;
    private static int _toastRow;

    // --- ANSI/VT support ---------------------------------------------------------
    private static bool _ansiEnabled = false;

    // P/Invoke (Windows only)
    private const int STD_OUTPUT_HANDLE = -11;
    private const int ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int nStdHandle);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out int lpMode);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(IntPtr hConsoleHandle, int dwMode);


    public static void Init()
    {
        // On non-Windows, most terminals support ANSI by default
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _ansiEnabled = true;
            return;
        }

        // Try to enable Virtual Terminal Processing on Windows 10+
        try
        {
            IntPtr handle = GetStdHandle(STD_OUTPUT_HANDLE);
            if (GetConsoleMode(handle, out int mode))
            {
                mode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
                if (SetConsoleMode(handle, mode))
                    _ansiEnabled = true;
            }
        }
        catch
        {
            _ansiEnabled = false;
        }
    }

    // ----------------------------------------------------------------------------

    public static void Clear()
    {
        try { Console.Clear(); } catch { /* ignore in some terminals */ }
    }

    public static void Cleanup()
    {
        if (_ansiEnabled)
            Console.Write("\x1b[0m");
        Console.ResetColor();
        Console.CursorVisible = true;
        Console.Clear();
        _toastUntilUtc = null;
    }

    public static void Write(int row, int col, string text,
        ConsoleColor? fg = null, ConsoleColor? bg = null, TextStyle style = TextStyle.None)
    {
        SafeSetPos(row, col);
        
        if (_ansiEnabled)
        {
            string sgr = BuildSgr(style, fg, bg);
            Console.Write(sgr);
            Console.Write(text);
            Console.Write("\x1b[0m");
        }
        else
        {
            if (bg.HasValue) Console.BackgroundColor = bg.Value;
            if (fg.HasValue) Console.ForegroundColor = fg.Value;
        
            Console.Write(text);
            Console.ResetColor();
        }
    }

    public static void WriteField(int row, int col, string text, int maxWidth,
        ConsoleColor? fg = null, ConsoleColor? bg = null, TextStyle style = TextStyle.None)
    {
        string s = text.Length > maxWidth ? text[..maxWidth] : text.PadRight(maxWidth);
        Write(row, col, s, fg, bg, style);
    }

    public static void WriteCentered(int row, string text, 
        ConsoleColor? fg = null, ConsoleColor? bg = null, bool bold = false, TextStyle style = TextStyle.None)
    {
        if (bold)
            style |= TextStyle.Bold;
        int width = Math.Max(1, Console.WindowWidth);
        int col = Math.Max(0, (width - text.Length) / 2);
        if (bold) text = $"{text}";
        Write(row, col, text, fg, bg, style);
    }

    public static void Toast(string message, int milliseconds = 4_000)
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
        => Columns(row, startCol, null, null, TextStyle.None, cols);

    public static void Columns(int row, int startCol, ConsoleColor fg, params (string text, int width, bool right)[] cols)
        => Columns(row, startCol, fg, null, TextStyle.None, cols);

    public static void Columns(int row, int startCol, ConsoleColor fg, TextStyle style, params (string text, int width, bool right)[] cols)
        => Columns(row, startCol, fg, null, style, cols);

    public static void Columns(int row, int startCol,
        ConsoleColor? fg, ConsoleColor? bg, TextStyle style = TextStyle.None,
        params (string text, int width, bool right)[] cols)
    {
        int col = startCol;
        foreach (var (text, width, right) in cols)
        {
            string cell = text ?? "";
            // truncate
            if (cell.Length > width)
                cell = cell[..width];
            // pad
            cell = right ? cell.PadLeft(width) : cell.PadRight(width);
            Write(row, col, cell, fg, bg, style);
            col += width + 2; // 2 spaces gap
        }
    }
    // -------------------------------------------------------------------------------

    private static void SafeSetPos(int row, int col)
    {
        row = Math.Clamp(row, 0, Math.Max(0, Console.WindowHeight - 1));
        col = Math.Clamp(col, 0, Math.Max(0, Console.WindowWidth - 1));
        try { Console.SetCursorPosition(col, row); } catch { /* terminal might be too small */ }
    }

    private static string BuildSgr(TextStyle style, ConsoleColor? fg, ConsoleColor? bg)
    {
        var parts = new List<string>();

        // styles
        if (style.HasFlag(TextStyle.Bold))      parts.Add("1");
        if (style.HasFlag(TextStyle.Dim))       parts.Add("2");
        if (style.HasFlag(TextStyle.Italic))    parts.Add("3");
        if (style.HasFlag(TextStyle.Underline)) parts.Add("4");
        if (style.HasFlag(TextStyle.Blink))     parts.Add("5");
        if (style.HasFlag(TextStyle.Invert))    parts.Add("7");

        // colors
        if (fg.HasValue) parts.Add(MapColor(fg.Value, isBg: false));
        if (bg.HasValue) parts.Add(MapColor(bg.Value, isBg: true));

        if (parts.Count == 0) return ""; // nothing special
        return "\x1b[" + string.Join(';', parts) + "m";
    }

    private static string MapColor(ConsoleColor color, bool isBg)
    {
        // Map ConsoleColor to SGR codes. We favor bright variants for the high 8 colors.
        int baseCode = isBg ? 40 : 30;
        int brightBase = isBg ? 100 : 90;

        return color switch
        {
            ConsoleColor.Black       => $"{baseCode + 0}",
            ConsoleColor.DarkRed     => $"{baseCode + 1}",
            ConsoleColor.DarkGreen   => $"{baseCode + 2}",
            ConsoleColor.DarkYellow  => $"{baseCode + 3}",
            ConsoleColor.DarkBlue    => $"{baseCode + 4}",
            ConsoleColor.DarkMagenta => $"{baseCode + 5}",
            ConsoleColor.DarkCyan    => $"{baseCode + 6}",
            ConsoleColor.Gray        => $"{baseCode + 7}",

            ConsoleColor.DarkGray    => $"{brightBase + 0}",
            ConsoleColor.Red         => $"{brightBase + 1}",
            ConsoleColor.Green       => $"{brightBase + 2}",
            ConsoleColor.Yellow      => $"{brightBase + 3}",
            ConsoleColor.Blue        => $"{brightBase + 4}",
            ConsoleColor.Magenta     => $"{brightBase + 5}",
            ConsoleColor.Cyan        => $"{brightBase + 6}",
            ConsoleColor.White       => $"{brightBase + 7}",

            _ => $"{baseCode + 7}"
        };
    }
}