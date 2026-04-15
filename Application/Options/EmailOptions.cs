namespace MyBackend.Application.Options;

public class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 465;
    public bool UseSsl { get; set; } = true;

    public string SenderName { get; set; } = "Aksa Digitex CFO";
    public string SenderEmail { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;

    public bool EnableHttpFallback { get; set; } = false;
    public string HttpFallbackProvider { get; set; } = string.Empty;
    public string HttpFallbackApiUrl { get; set; } = string.Empty;
    public string HttpFallbackApiKey { get; set; } = string.Empty;
    public string HttpFallbackFromEmail { get; set; } = string.Empty;
    public string HttpFallbackFromName { get; set; } = string.Empty;
    public int HttpFallbackTimeoutSeconds { get; set; } = 15;
}