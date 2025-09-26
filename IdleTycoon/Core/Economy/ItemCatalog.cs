namespace IdleTycoon.Core.Economy;

public sealed class ItemCatalog
{
    public List<ItemDef> All = new();

    public ItemDef ById(string id) => All.First(x => x.Id == id);

    public static ItemCatalog Default()
    {
        return new ItemCatalog
        {
            All = new List<ItemDef>
            {
                new("cursor", "Cursor",     10,   1.15, 0.1),
                new("worker", "Worker",     100,   1.15, 1),
                new("farm",   "Farm",      1_000, 1.15, 8),
                new("factory","Factory",  10_000, 1.15, 47),
                new("lab",     "Lab",     100_000, 1.15, 260),
            }
        };
    }
}
