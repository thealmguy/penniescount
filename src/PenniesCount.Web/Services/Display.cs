using System.Globalization;
using PenniesCount.Contracts;

namespace PenniesCount.Web.Services;

public static class Display
{
    private static readonly CultureInfo Gbp = CultureInfo.GetCultureInfo("en-GB");
    public static string Money(decimal amount) => amount.ToString("C2", Gbp);
    public static string SavingsCategoryName(SavingsTerm type) => type switch
    {
        SavingsTerm.Short => "Short term",
        SavingsTerm.Medium => "Medium term",
        SavingsTerm.Long => "Long term",
        SavingsTerm.Past => "Past savings goals",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
    public static string AccountTypeName(AccountType type) => type switch
    {
        AccountType.CashAccount => "Cash Account",
        AccountType.CashIsa => "Cash ISA",
        AccountType.StocksAndSharesIsa => "Stocks and Shares ISA",
        AccountType.InvestmentAccount => "Investment Account",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
    public static string ContributionName(ContributionState status) => status switch
    {
        ContributionState.Funded => "Fully allocated",
        ContributionState.NotStarted => "Not started",
        ContributionState.Scheduled => "Monthly",
        ContributionState.NeededNow => "Needed now",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}
