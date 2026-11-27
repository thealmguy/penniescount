using PenniesCount.Api.Data;

namespace PenniesCount.Api;

internal static class LedgerRuleResults
{
    public static IResult ToResult(this LedgerRuleViolation violation, ILogger logger) => violation.Kind switch
    {
        LedgerRuleKind.NotFound => Results.NotFound(),
        LedgerRuleKind.Conflict => ApiValidation.Conflict(logger),
        LedgerRuleKind.Removed => Removed(logger),
        LedgerRuleKind.AlreadyReversed => AlreadyReversed(logger),
        _ => Invalid(violation, logger)
    };

    private static IResult Removed(ILogger logger)
    {
        logger.LogWarning("A write to a removed savings item was rejected.");
        return Results.Problem(statusCode: StatusCodes.Status409Conflict,
            title: "Removed items are read-only.");
    }

    private static IResult AlreadyReversed(ILogger logger)
    {
        logger.LogWarning("A write to an already reversed ledger entry was rejected.");
        return Results.Problem(statusCode: StatusCodes.Status409Conflict,
            title: "This entry has already been reversed. Reload the history and retry.");
    }

    private static IResult Invalid(LedgerRuleViolation violation, ILogger logger)
    {
        logger.LogWarning("{Rule} validation failed.", violation.Rule);
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [violation.Field!] = [violation.Message!]
        });
    }
}
