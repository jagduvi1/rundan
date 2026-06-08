namespace Rundan.Client.Services;

/// <summary>Thrown when an API call fails; carries a user-friendly message and status code.</summary>
public sealed class ApiException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public bool IsUnauthorized => StatusCode == 401;
    public bool IsForbidden => StatusCode == 403;
    public bool IsNotFound => StatusCode == 404;
}
