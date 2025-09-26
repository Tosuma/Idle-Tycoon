using IdleTycoon.Core.Economy;

namespace IdleTycoon.Gameplay.CampaignDesign;

public record LevelDef(string Id, string Name, double GoalMoney, List<ItemDef> Producers);
