using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using MyBackend.Application.Options;

namespace MyBackend.Application.Services;

public class SmtpEmailService : IEmailService
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IOptions<EmailOptions> options, ILogger<SmtpEmailService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(SendEmailRequest request, CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();

        if (string.IsNullOrWhiteSpace(request.ToEmail))
            throw new ArgumentException("ToEmail is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Subject))
            throw new ArgumentException("Subject is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.HtmlBody))
            throw new ArgumentException("HtmlBody is required.", nameof(request));

        using var message = new MailMessage
        {
            From = new MailAddress(_options.SenderEmail, _options.SenderName),
            Subject = request.Subject,
            Body = request.HtmlBody,
            IsBodyHtml = true,
        };

        message.To.Add(new MailAddress(request.ToEmail, request.ToName));

        if (!string.IsNullOrWhiteSpace(request.PlainTextBody))
        {
            message.AlternateViews.Add(
                AlternateView.CreateAlternateViewFromString(request.PlainTextBody, null, "text/plain"));
        }

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl,
            Credentials = new NetworkCredential(_options.Username, _options.Password),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = Math.Max(_options.TimeoutSeconds, 1) * 1000,
        };

        _logger.LogInformation("Sending email to {ToEmail} with subject {Subject}", request.ToEmail, request.Subject);
        await client.SendMailAsync(message, cancellationToken);
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
            throw new InvalidOperationException("Email:Host is required.");
        if (_options.Port <= 0)
            throw new InvalidOperationException("Email:Port must be greater than 0.");
        if (string.IsNullOrWhiteSpace(_options.SenderEmail))
            throw new InvalidOperationException("Email:SenderEmail is required.");
        if (string.IsNullOrWhiteSpace(_options.Username))
            throw new InvalidOperationException("Email:Username is required.");
        if (string.IsNullOrWhiteSpace(_options.Password))
            throw new InvalidOperationException("Email:Password is required.");
    }
}