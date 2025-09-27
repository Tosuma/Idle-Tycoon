namespace IdleTycoon.Ui.ConsoleUI;
public static class UI
{
    private static DateTime? _toastUntilUtc;
    private static int _toastRow;

    public static void Clear()
    {
        try { Console.Clear(); } catch { /* ignore in some terminals */ }
    }

    public static void Cleanup()
    {
        Console.ResetColor();
        Console.CursorVisible = true;
        Console.Clear();
        _toastUntilUtc = null;
    }

    public static void Write(int row, int col, string text, ConsoleColor? fg = null, ConsoleColor? bg = null)
    {
        SafeSetPos(row, col);
        if (bg.HasValue) Console.BackgroundColor = bg.Value;
        if (fg.HasValue) Console.ForegroundColor = fg.Value;
        Console.Write(text);
        Console.ResetColor();
    }

    public static void WriteField(int row, int col, string text, int maxWidth)
    {
        string s = text.Length > maxWidth ? text[..maxWidth] : text.PadRight(maxWidth);
        Write(row, col, s);
    }

    public static void WriteCentered(int row, string text, ConsoleColor? fg = null, ConsoleColor? bg = null, bool bold = false)
    {
        int width = Math.Max(1, Console.WindowWidth);
        int col = Math.Max(0, (width - text.Length) / 2);
        if (bold) text = $"{text}";
        Write(row, col, text, fg, bg);
    }

    public static void Toast(string message, int milliseconds = 4000)
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
        => Columns(row, startCol, null, null, cols);

    public static void Columns(int row, int startCol, ConsoleColor fg, params (string text, int width, bool right)[] cols)
        => Columns(row, startCol, fg, null, cols);

    public static void Columns(int row, int startCol, ConsoleColor? fg, ConsoleColor? bg,
        params (string text, int width, bool right)[] cols)
    {
        int col = startCol;
        foreach (var c in cols)
        {
            string cell = c.text ?? "";
            // truncate
            if (cell.Length > c.width)
                cell = cell[..c.width];
            // pad
            cell = c.right ? cell.PadLeft(c.width) : cell.PadRight(c.width);
            Write(row, col, cell, fg, bg);
            col += c.width + 2; // 2 spaces gap
        }
    }
    // -------------------------------------------------------------------------------

    private static void SafeSetPos(int row, int col)
    {
        row = Math.Clamp(row, 0, Math.Max(0, Console.WindowHeight - 1));
        col = Math.Clamp(col, 0, Math.Max(0, Console.WindowWidth - 1));
        try { Console.SetCursorPosition(col, row); } catch { /* terminal might be too small */ }
    }
}