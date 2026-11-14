namespace CsRaiders.Api.Infrastructure.Options;

public sealed record EmailOptions
{
    public const string SectionName = "Email";

    public string DeliveryMode { get; set; } = "Console";
    public SmtpOptions Smtp { get; set; } = new();
}

public sealed record SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string SenderEmail { get; set; } = "no-reply@csraiders.com";
    public string SenderName { get; set; } = "CS Raiders";
    public int TimeoutSeconds { get; set; } = 15;
}
