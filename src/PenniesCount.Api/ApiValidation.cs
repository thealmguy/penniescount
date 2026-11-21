using System.ComponentModel.DataAnnotations;

namespace PenniesCount.Api;

internal static class ApiValidation
{
    public static IResult? Check(object request, ILogger logger)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(request, new ValidationContext(request), results, true))
            return null;

        logger.LogWarning("API request validation failed.");
        var errors = results
            .SelectMany(result => result.MemberNames.DefaultIfEmpty("Request")
                .Select(member => (member, message: result.ErrorMessage ?? "Invalid value.")))
            .GroupBy(x => x.member)
            .ToDictionary(group => group.Key, group => group.Select(x => x.message).ToArray());
        return Results.ValidationProblem(errors);
    }

    public static IResult Conflict(ILogger logger)
    {
        logger.LogWarning("A stale API update was rejected.");
        return Results.Problem(statusCode: StatusCodes.Status409Conflict,
            title: "This record changed. Reload it and retry your update.");
    }
}
