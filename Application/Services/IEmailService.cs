namespace MyBackend.Application.Services;

public sealed class SendEmailRequest
{
    public string ToEmail { get; set; } = string.Empty;
    public string? ToName { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    public string? PlainTextBody { get; set; }
}

public interface IEmailService
{
    Task SendAsync(SendEmailRequest request, CancellationToken cancellationToken = default);
}