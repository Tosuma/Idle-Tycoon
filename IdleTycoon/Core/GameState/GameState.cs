using IdleTycoon.Core.Economy;
using IdleTycoon.Gameplay;

namespace IdleTycoon.Core.GameState;

public sealed class GameState
{
    public double Money { get; set; }
    public double LifetimeEarnings { get; set; } = 0; // cumulative earnings across runs
    public int Prestiges { get; set; } = 0;
    public int PrestigeCredits { get; set; } = 0;
    public int PrestigeCreditsEarnedHistorical { get; set; } = 0; // bookkeeping so we only award new credits

    public string CurrentLevelId { get; set; } = "lvl1";
    public HashSet<string> UnlockedLevels { get; set; } = new() { "lvl1" };

    public List<ItemState> Items { get; set; } = new();

    public static GameState New()
    {
        return new GameState
        {
            Money = 0,
            LifetimeEarnings = 0,
            Prestiges = 0,
            PrestigeCredits = 0,
            PrestigeCreditsEarnedHistorical = 0,
            CurrentLevelId = "lvl1",
            UnlockedLevels = new HashSet<string> { "lvl1" },
            Items = new List<ItemState>()
        };
    }

    public ItemState GetItemState(string id)
    {
        var found = Items.FirstOrDefault(i => i.ItemId == id);
        if (found == null)
        {
            found = new ItemState { ItemId = id, Quantity = 0 };
            Items.Add(found);
        }
        return found;
    }

    public double TotalProductionPerSecond(ItemCatalog catalog)
    {
        double total = 0;
        foreach (var st in Items)
        {
            if (st.Quantity <= 0) continue;
            var def = catalog.ById(st.ItemId);
            total += def.BaseProductionPerSecond * st.Quantity * Upgrades.MultiplierFor(st.UpgradeLevel);
        }
        total *= Prestige.ProdMultiplier(PrestigeCredits);
        return total;
    }
}
