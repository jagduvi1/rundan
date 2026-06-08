namespace Rundan.Server.Services;

/// <summary>
/// Thrown when a game rule is broken (wrong status, not your activity, etc.).
/// Translated to a ProblemDetails response with <see cref="StatusCode"/> by
/// <see cref="RuleViolationExceptionHandler"/>.
/// </summary>
public sealed class RuleViolationException(string message, int statusCode = StatusCodes.Status400BadRequest)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
