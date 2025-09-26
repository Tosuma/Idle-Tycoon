namespace IdleTycoon.Core.GameState;

public sealed class ItemState
{
    public string ItemId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int UpgradeLevel { get; set; } = 0; // per-item leveling within a run
}
