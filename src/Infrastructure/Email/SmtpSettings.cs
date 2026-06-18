namespace Template.Infrastructure.Email;

public class SmtpSettings
{
    public required string Host { get; init; }
    public int Port { get; init; } = 1025;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public required string FromAddress { get; init; }
    public string FromName { get; init; } = "App";
}
