using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Rundan.Server.Services;

/// <summary>Turns <see cref="RuleViolationException"/> into a clean ProblemDetails response.</summary>
public sealed class RuleViolationExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not RuleViolationException rule)
        {
            return false; // let the default handler produce a 500
        }

        httpContext.Response.StatusCode = rule.StatusCode;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = rule,
            ProblemDetails = new ProblemDetails
            {
                Status = rule.StatusCode,
                Title = "Cannot complete the action",
                Detail = rule.Message,
            },
        });
    }
}
