using IdleTycoon.Core.Economy;

namespace IdleTycoon.Gameplay;

public static class Upgrades
{
    // Production multiplier per upgrade level (e.g., Lv1=1.5x, Lv2=2.25x, ...)
    public static double MultiplierFor(int level) => Math.Pow(1.5, level);

    // Upgrade price scales off the item's base cost and current level.
    public static double UpgradePrice(ItemDef def, int currentLevel)
        => def.BaseCost * 5 * Math.Pow(1.7, currentLevel);
}
