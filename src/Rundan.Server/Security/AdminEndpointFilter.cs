namespace Rundan.Server.Security;

/// <summary>
/// Endpoint filter that requires the admin code (header <c>X-Rundan-Admin</c>) for
/// activity management endpoints. If no admin code is configured, admin actions are
/// open (intended only for local development).
/// </summary>
public sealed class AdminEndpointFilter(RundanOptions options) : IEndpointFilter
{
    public const string HeaderName = "X-Rundan-Admin";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (options.RequiresAdminCode)
        {
            var provided = context.HttpContext.Request.Headers[HeaderName].ToString();
            if (!SecurityHelpers.FixedEquals(provided, options.AdminCode))
            {
                return Results.Problem(
                    title: "Admin code required",
                    detail: "This action needs the admin code.",
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }

        return await next(context);
    }
}
