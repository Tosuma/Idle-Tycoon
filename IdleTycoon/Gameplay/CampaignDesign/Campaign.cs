using IdleTycoon.Core.GameState;

namespace IdleTycoon.Gameplay.CampaignDesign;

public static class Campaign
{
    public static readonly List<LevelDef> Levels = new()
    {
        new("lvl1", "Desert Outskirts", 1_000_000, new()
        {
            new("vaporator","Moisture Vaporator",10,1.15,0.10),
            new("junkdroid","Salvage Droid",60,1.15,0.70),
            new("landspeeder","Landspeeder Runs",600,1.15,6),
            new("cantina","Cantina Stalls",6_000,1.15,45),
            new("freighter","Small Freighter",60_000,1.15,300),
        }),
        new("lvl2", "Spaceport Fringe", 50_000_000, new()
        {
            new("droidshop","Droid Workshop",200,1.15,1.8),
            new("hangar","Hangar Bay",2_000,1.15,14),
            new("market","Bazaar Network",20_000,1.15,95),
            new("courier","Courier Routes",200_000,1.15,620),
            new("guild","Guild Contracts",2_000_000,1.15,4_000),
        }),
        new("lvl3", "Rebel Outpost", 2_000_000_000, new()
        {
            new("lookout","Lookout Posts",1_000,1.15,3.5),
            new("comms","Comms Relay",10_000,1.15,26),
            new("cells","Rebel Cells",100_000,1.15,180),
            new("supply","Supply Lines",1_000_000,1.15,1_150),
            new("wing","Starfighter Wing",10_000_000,1.15,7_200),
        }),
        new("lvl4", "Icy Holdfast", 80_000_000_000, new()
        {
            new("patrol","Perimeter Patrols",6_000,1.15,20),
            new("shield","Field Generators",60_000,1.15,140),
            new("depot","Supply Depot",600_000,1.15,980),
            new("hanger","Heavy Hangars",6_000_000,1.15,6_600),
            new("convoy","Convoy Command",60_000_000,1.15,45_000),
        }),
        new("lvl5", "Cloud Mines", 3_000_000_000_000, new()
        {
            new("lift","Gas Lifters",30_000,1.15,70),
            new("refine","Refinery Lines",300_000,1.15,500),
            new("trade","Trade Consortium",3_000_000,1.15,3_400),
            new("harbor","Sky Harbor",30_000_000,1.15,22_000),
            new("charter","Charter Fleets",300_000_000,1.15,150_000),
        }),
        new("lvl6", "Capital Shipyards", 120_000_000_000_000, new()
        {
            new("drydock","Drydock Crews",150_000,1.15,300),
            new("frames","Hull Frames",1_500_000,1.15,2_100),
            new("yards","Orbital Yards",15_000_000,1.15,14_000),
            new("fleets","Fleet Logistics",150_000_000,1.15,95_000),
            new("capital","Capital Lines",1_500_000_000,1.15,650_000),
        }),
    };

    public static LevelDef Current(GameState s)
        => Levels.First(l => l.Id == s.CurrentLevelId);

    public static LevelDef? Next(GameState s)
    {
        var i = Levels.FindIndex(l => l.Id == s.CurrentLevelId);
        return i >= 0 && i < Levels.Count - 1 
            ? Levels[i + 1] 
            : null;
    }

    public static LevelDef ResetToFirstLevel(GameState s)
    {
        var first = Levels[0];
        s.CurrentLevelId = first.Id;
        s.UnlockedLevels.Clear();
        s.UnlockedLevels.Add(first.Id);
        s.Money = 0;
        s.Items.Clear();

        var st = s.GetItemState(Levels[0].Id);
        st.Quantity = 1;
        return first;
    }

    public static void ProgressToNextLevel(GameState s)
    {
        var next = Next(s);
        // Advance: unlock & switch, reset run state
        if (next is not null)
        {
            s.UnlockedLevels.Add(next.Id);
            s.CurrentLevelId = next.Id;
            s.Money = 0;
            s.Items.Clear();
            var lowestProducer = Current(s).Producers.FirstOrDefault();
            if (lowestProducer is not null)
            {
                var st = s.GetItemState(lowestProducer.Id);
                st.Quantity = 1;
            }
        }

    }
}
