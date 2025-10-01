using IdleTycoon.Core.GameState;
using IdleTycoon.Gameplay.CampaignDesign;
using static System.Net.Mime.MediaTypeNames;

namespace IdleTycoon.Gameplay;

public static class Prestige
{
    private const double _baseThreshold = 1_000_000d;

    public static double NextCreditThresholdFromLifetime(double lifetimeEarnings, double baseThreshold = _baseThreshold)
    {
        if (lifetimeEarnings < baseThreshold)
            return baseThreshold;

        // totalCredits = floor(log10(L/base)) + 1
        int totalCredits = (int)Math.Floor(Math.Log10(lifetimeEarnings) - Math.Log10(baseThreshold) + 1);

        // Next threshold is the next power of 10 above the current credit band.
        return baseThreshold * Math.Pow(10, totalCredits);
    }

    public static double NextNewCreditThreshold(GameState s)
        => NextNewCreditThreshold(s.LifetimeEarnings, s.PrestigeCreditsEarnedHistorical);

    public static double NextNewCreditThreshold(double lifetimeEarnings, int alreadyBanked, double baseThreshold = 1_000_000d)
    {
        if (alreadyBanked < 0)
            alreadyBanked = 0;

        if (lifetimeEarnings < baseThreshold)
            return baseThreshold;

        // Total credits unlocked by lifetime
        int totalCredits = (int)Math.Floor(Math.Log10(lifetimeEarnings) - Math.Log10(baseThreshold) + 1);

        // If not yet eligible for a NEW credit, the next target is the first threshold not yet banked
        // Otherwise, it's the threshold after the current totalCredits.
        int neededIndex = Math.Max(alreadyBanked, totalCredits);
        return baseThreshold * Math.Pow(10, neededIndex);
    }

    public static double RemainingToNextNewCredit(GameState s)
        => RemainingToNextNewCredit(s.LifetimeEarnings, s.PrestigeCreditsEarnedHistorical);

    public static double RemainingToNextNewCredit(double lifetimeEarnings, int alreadyBanked, double baseThreshold = 1_000_000d)
        => Math.Max(
            0,
            NextNewCreditThreshold(lifetimeEarnings, alreadyBanked, baseThreshold) - lifetimeEarnings
        );

    public static int CreditsEarnedNow(GameState s)
        => PotentialCredits(s.LifetimeEarnings, s.PrestigeCreditsEarnedHistorical);

    public static int PotentialCredits(double lifetimeEarnings, int alreadyBanked, double baseThreshold = _baseThreshold)
    {
        if (lifetimeEarnings < baseThreshold) return 0;
        int totalCredits = (int)Math.Floor(Math.Log10(lifetimeEarnings) - Math.Log10(baseThreshold) + 1);
        int newCredits = totalCredits - alreadyBanked;
        return Math.Max(0, newCredits);
    }

    public static double ProdMultiplier(int credits) => 1.0 + 0.05 * credits;

    public static int ApplyReset(GameState s)
    {
        int earned = CreditsEarnedNow(s);
        if (earned <= 0) return 0;
        s.PrestigeCredits += earned;
        s.PrestigeCreditsEarnedHistorical += earned;
        s.Prestiges += 1;
        Campaign.ResetToFirstLevel(s);
        return earned;
    }
}
