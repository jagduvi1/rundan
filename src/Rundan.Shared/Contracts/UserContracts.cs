namespace Rundan.Shared.Contracts;

/// <summary>A pre-registered roster user.</summary>
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class CreateUserRequest
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>Result of an admin image upload.</summary>
public class UploadResultDto
{
    public string Url { get; set; } = string.Empty;
}
