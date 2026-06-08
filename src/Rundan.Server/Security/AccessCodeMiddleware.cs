namespace Rundan.Server.Security;

/// <summary>
/// Gates the API and SignalR hub behind the shared site access code. The Blazor
/// static files (HTML/JS/WASM) are intentionally not gated — there is nothing
/// sensitive in them; all data access goes through the gated API/hub.
/// </summary>
public sealed class AccessCodeMiddleware(RequestDelegate next, RundanOptions options)
{
    public const string HeaderName = "X-Rundan-Access";

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;

        var gated =
            (path.StartsWithSegments("/api") && !path.StartsWithSegments("/api/bootstrap"))
            || path.StartsWithSegments("/hub");

        if (gated && options.RequiresAccessCode)
        {
            // Browsers can't set custom headers on the WebSocket handshake, so SignalR
            // sends the code via the access_token query string.
            var provided = context.Request.Headers[HeaderName].ToString();
            if (string.IsNullOrEmpty(provided))
            {
                provided = context.Request.Query["access_token"].ToString();
            }

            if (string.IsNullOrEmpty(provided))
            {
                // SignalR's negotiate POST (and HTTP long-polling) carries the
                // AccessTokenProvider value as an "Authorization: Bearer" header, not the
                // access_token query string. Without this, the gated hub never connects.
                var auth = context.Request.Headers.Authorization.ToString();
                if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    provided = auth["Bearer ".Length..];
                }
            }

            if (!SecurityHelpers.FixedEquals(provided, options.AccessCode))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing access code." });
                return;
            }
        }

        await next(context);
    }
}
