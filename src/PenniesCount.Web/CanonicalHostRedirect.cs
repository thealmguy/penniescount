using Microsoft.AspNetCore.Http.Extensions;

namespace PenniesCount.Web;

public static class CanonicalHostRedirect
{
    // Permanently redirects every other host (www, the platform default hostname) to the
    // configured canonical host over HTTPS. Without CanonicalHost configured it is inert.
    public static WebApplication UseCanonicalHostRedirect(this WebApplication app)
    {
        var canonicalHost = app.Configuration["CanonicalHost"];
        if (string.IsNullOrWhiteSpace(canonicalHost))
            return app;
        var target = new HostString(canonicalHost);
        app.Use((context, next) =>
        {
            var request = context.Request;
            if (string.Equals(request.Host.Host, target.Host, StringComparison.OrdinalIgnoreCase))
                return next(context);
            context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
            context.Response.Headers.Location =
                UriHelper.BuildAbsolute("https", target, request.PathBase, request.Path, request.QueryString);
            return Task.CompletedTask;
        });
        return app;
    }
}
