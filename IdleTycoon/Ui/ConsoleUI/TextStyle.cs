namespace IdleTycoon.Ui.ConsoleUI;

[Flags]
public enum TextStyle
{
    None      = 0,
    Bold      = 1 << 0, // SGR 1
    Dim       = 1 << 1, // SGR 2
    Italic    = 1 << 2, // SGR 3
    Underline = 1 << 3, // SGR 4
    Blink     = 1 << 4, // SGR 5 (usually not supported)
    Invert    = 1 << 5, // SGR 7
}
