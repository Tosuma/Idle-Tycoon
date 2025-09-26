namespace IdleTycoon.Core.Numbers;

public static class NumFmt
{
    public static string Format(double value, int decimals = 2)
    {
        double abs = Math.Abs(value);
        if (abs < 1000) return value.ToString($"0.{new string('0', decimals)}");

        int tier = (int)Math.Floor(Math.Log10(abs) / 3.0); // thousands groups
        double scaled = value / Math.Pow(1000, tier);
        string suffix = tier switch
        {
            1 => "K",
            2 => "M",
            3 => "B",
            _ => AlphaSuffix(tier - 4) // 10^12 => tier=4 => index 0 => "aa"
        };
        return Trim(scaled, decimals) + suffix;
    }

    private static string Trim(double v, int decimals)
    {
        string s = v.ToString($"0.{new string('#', decimals)}");
        return s;
    }

    private static string AlphaSuffix(int index)
    {
        if (index < 0) return "";
        var chars = new List<char>();
        int n = index;
        do
        {
            int r = n % 26; // 0..25
            chars.Add((char)('a' + r));
            n /= 26;
        } while (n > 0);
        chars.Reverse();
        while (chars.Count < 2) chars.Insert(0, 'a');
        return new string(chars.ToArray());
    }
}
