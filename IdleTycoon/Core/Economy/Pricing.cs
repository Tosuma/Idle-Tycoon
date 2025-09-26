namespace IdleTycoon.Core.Economy;

public static class Pricing
{
    public static double CurrentPrice(ItemDef def, int owned)
        => def.BaseCost * Math.Pow(def.CostMultiplier, owned);
}
