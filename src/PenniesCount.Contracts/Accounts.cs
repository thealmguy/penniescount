using System.ComponentModel.DataAnnotations;

namespace PenniesCount.Contracts;

public enum AccountType { CashAccount, CashIsa, StocksAndSharesIsa, InvestmentAccount }

public sealed class CreateAccountRequest
{
    [Required, StringLength(100)]
    public string ProviderName { get; set; } = "";

    [Required, StringLength(100)]
    public string AccountName { get; set; } = "";

    [EnumDataType(typeof(AccountType))]
    public AccountType Type { get; set; }

    [Money]
    public decimal TotalFunds { get; set; }
}

public sealed class UpdateBalanceRequest
{
    [Money]
    public decimal TotalFunds { get; set; }

    [Required, RegularExpression("^[A-Za-z0-9+/]{11}=$")]
    public string Version { get; set; } = "";
}

public sealed record AccountResponse(Guid Id, string ProviderName, string AccountName,
    AccountType Type, decimal TotalFunds, string Version);

public sealed record BalanceSnapshotResponse(long Id, decimal TotalFunds, DateTimeOffset RecordedAtUtc);
