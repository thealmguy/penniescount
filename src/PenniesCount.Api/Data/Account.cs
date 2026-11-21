using PenniesCount.Contracts;

namespace PenniesCount.Api.Data;

public sealed class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProviderName { get; set; } = "";
    public string AccountName { get; set; } = "";
    public AccountType Type { get; set; }
    public decimal TotalFunds { get; set; }
    public byte[] Version { get; set; } = [];
    public List<AccountBalanceSnapshot> History { get; set; } = [];

    public AccountResponse ToResponse() =>
        new(Id, ProviderName, AccountName, Type, TotalFunds, Convert.ToBase64String(Version));
}

public sealed class AccountBalanceSnapshot
{
    public long Id { get; set; }
    public Guid AccountId { get; set; }
    public decimal TotalFunds { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}
