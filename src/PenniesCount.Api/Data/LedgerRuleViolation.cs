namespace PenniesCount.Api.Data;

public enum LedgerRuleKind
{
    NotFound,
    Invalid,
    Conflict,
    Removed,
    AlreadyReversed
}

public sealed record LedgerRuleViolation(LedgerRuleKind Kind, string? Field = null, string? Message = null,
    string? Rule = null)
{
    public static readonly LedgerRuleViolation NotFound = new(LedgerRuleKind.NotFound);
    public static readonly LedgerRuleViolation Conflict = new(LedgerRuleKind.Conflict);
    public static readonly LedgerRuleViolation Removed = new(LedgerRuleKind.Removed);
    public static readonly LedgerRuleViolation AlreadyReversed = new(LedgerRuleKind.AlreadyReversed);

    public static LedgerRuleViolation Invalid(string field, string message, string rule) =>
        new(LedgerRuleKind.Invalid, field, message, rule);
}
