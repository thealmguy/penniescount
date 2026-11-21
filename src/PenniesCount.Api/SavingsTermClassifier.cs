using PenniesCount.Contracts;

namespace PenniesCount.Api;

internal static class SavingsTermClassifier
{
    public static SavingsTerm Classify(DateOnly targetDate, DateOnly asOf)
    {
        if (targetDate < asOf) return SavingsTerm.Past;
        if (asOf.Year == 9999 || targetDate < asOf.AddYears(1)) return SavingsTerm.Short;
        if (asOf.Year > 9994 || targetDate <= asOf.AddYears(5)) return SavingsTerm.Medium;
        return SavingsTerm.Long;
    }
}
