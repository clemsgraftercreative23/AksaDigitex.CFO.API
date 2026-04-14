using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using MyBackend.Application.Options;

namespace MyBackend.Application.Services;

public class SmtpEmailService : IEmailService
{
    private readonly EmailOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(
        IOptions<EmailOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<SmtpEmailService> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
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

        if (string.IsNullOrWhiteSpace(request.PlainTextBody))
        {
            message.AlternateViews.Add(
                AlternateView.CreateAlternateViewFromString(request.HtmlBody, Encoding.UTF8, "text/html"));
        }

        if (!string.IsNullOrWhiteSpace(request.PlainTextBody))
        {
            message.AlternateViews.Add(
                AlternateView.CreateAlternateViewFromString(request.HtmlBody, Encoding.UTF8, "text/html"));
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

        _logger.LogInformation(
            "Sending email to {ToEmail} with subject {Subject} via {Host}:{Port} ssl={UseSsl} timeout={TimeoutSeconds}s",
            request.ToEmail,
            request.Subject,
            _options.Host,
            _options.Port,
            _options.UseSsl,
            _options.TimeoutSeconds);

        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (SmtpException ex) when (ex.InnerException is SocketException socketEx && socketEx.SocketErrorCode == SocketError.TimedOut)
        {
            _logger.LogError(ex,
                "SMTP connect timeout to {Host}:{Port}. Likely network egress/firewall restriction from runtime environment.",
                _options.Host,
                _options.Port);

            if (CanUseHttpFallback())
            {
                _logger.LogWarning(
                    "SMTP failed; trying HTTP fallback provider={Provider}.",
                    _options.HttpFallbackProvider);

                await SendViaHttpFallbackAsync(request, cancellationToken);
                return;
            }

            throw new InvalidOperationException(
                $"SMTP connection timed out to {_options.Host}:{_options.Port}. Check provider egress policy/firewall and SMTP host reachability.",
                ex);
        }
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

    private bool CanUseHttpFallback()
    {
        return _options.EnableHttpFallback && !string.IsNullOrWhiteSpace(_options.HttpFallbackApiKey);
    }

    private async Task SendViaHttpFallbackAsync(SendEmailRequest request, CancellationToken cancellationToken)
    {
        var provider = (_options.HttpFallbackProvider ?? string.Empty).Trim().ToLowerInvariant();
        var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(Math.Max(_options.HttpFallbackTimeoutSeconds, 1));

        switch (provider)
        {
            case "resend":
                await SendViaResendAsync(http, request, cancellationToken);
                return;
            case "brevo":
                await SendViaBrevoAsync(http, request, cancellationToken);
                return;
            default:
                throw new InvalidOperationException(
                    "Email:HttpFallbackProvider must be 'resend' or 'brevo' when Email:EnableHttpFallback=true.");
        }
    }

    private async Task SendViaResendAsync(HttpClient http, SendEmailRequest request, CancellationToken cancellationToken)
    {
        var endpoint = string.IsNullOrWhiteSpace(_options.HttpFallbackApiUrl)
            ? "https://api.resend.com/emails"
            : _options.HttpFallbackApiUrl;

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.HttpFallbackApiKey);

        var fromEmail = string.IsNullOrWhiteSpace(_options.HttpFallbackFromEmail)
            ? _options.SenderEmail
            : _options.HttpFallbackFromEmail;
        var fromName = string.IsNullOrWhiteSpace(_options.HttpFallbackFromName)
            ? _options.SenderName
            : _options.HttpFallbackFromName;

        var payload = new
        {
            from = string.IsNullOrWhiteSpace(fromName) ? fromEmail : $"{fromName} <{fromEmail}>",
            to = new[] { request.ToEmail },
            subject = request.Subject,
            html = request.HtmlBody,
            text = request.PlainTextBody,
        };

        req.Content = JsonContent.Create(payload);
        using var res = await http.SendAsync(req, cancellationToken);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"HTTP fallback (resend) failed with {(int)res.StatusCode} {res.ReasonPhrase}. Body: {Truncate(body, 500)}");
        }

        _logger.LogInformation("Email sent via HTTP fallback provider=resend to {ToEmail}", request.ToEmail);
    }

    private async Task SendViaBrevoAsync(HttpClient http, SendEmailRequest request, CancellationToken cancellationToken)
    {
        var endpoint = string.IsNullOrWhiteSpace(_options.HttpFallbackApiUrl)
            ? "https://api.brevo.com/v3/smtp/email"
            : _options.HttpFallbackApiUrl;

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        req.Headers.Add("api-key", _options.HttpFallbackApiKey);

        var fromEmail = string.IsNullOrWhiteSpace(_options.HttpFallbackFromEmail)
            ? _options.SenderEmail
            : _options.HttpFallbackFromEmail;
        var fromName = string.IsNullOrWhiteSpace(_options.HttpFallbackFromName)
            ? _options.SenderName
            : _options.HttpFallbackFromName;

        var payload = new
        {
            sender = new { name = fromName, email = fromEmail },
            to = new[]
            {
                new
                {
                    email = request.ToEmail,
                    name = request.ToName,
                },
            },
            subject = request.Subject,
            htmlContent = request.HtmlBody,
            textContent = request.PlainTextBody,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var res = await http.SendAsync(req, cancellationToken);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"HTTP fallback (brevo) failed with {(int)res.StatusCode} {res.ReasonPhrase}. Body: {Truncate(body, 500)}");
        }

        _logger.LogInformation("Email sent via HTTP fallback provider=brevo to {ToEmail}", request.ToEmail);
    }

    private static string Truncate(string input, int max)
    {
        if (string.IsNullOrEmpty(input) || input.Length <= max) return input;
        return input[..max] + "...";
    }
}