using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using MyBackend.Application.Services;
using MyBackend.Infrastructure.Clients;

namespace MyBackend.Features.Notifications;

public static class NotificationsEndpoints
{
    /// <summary>Cache TTL for overdue notifications (per-user).</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    public sealed class SendTestEmailRequest
    {
        public string ToEmail { get; set; } = string.Empty;
        public string? ToName { get; set; }
                public string Subject { get; set; } = "PERINGATAN JATUH TEMPO: H-{H_MINUS}!";
                public bool UseTemplate { get; set; } = true;
                public string? HtmlBody { get; set; }
                public string? PlainTextBody { get; set; }
                public string JenisTagihan { get; set; } = "{jenis_tagihan}";
                public string BankName { get; set; } = "{bank_name}";
                public string TanggalJatuhTempo { get; set; } = "{tanggal_jatuh_tempo}";
                public string Nominal { get; set; } = "{nominal}";
                public string DocumentFileName { get; set; } = "Dokumen_Tagihan_{bank_name}.pdf";
                public string FileSize { get; set; } = "1.4 MB";
                public string ReminderTime { get; set; } = "10:00 WIB";
                public string DownloadUrl { get; set; } = "#";
    }

        private static string BuildDefaultEmailHtml(SendTestEmailRequest request)
        {
                return $$"""
<!doctype html>
<html lang="id">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>{{request.Subject}}</title>
</head>
<body style="margin:0;padding:0;background:#f0f2f4;font-family:Arial,Helvetica,sans-serif;color:#2d3748;">
    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f0f2f4;padding:20px 0;">
        <tr>
            <td align="center">
                <table role="presentation" width="620" cellpadding="0" cellspacing="0" style="width:620px;max-width:620px;background:#ffffff;border:1px solid #d9dee5;">
                    <tr>
                        <td style="background:#d9534f;color:#ffffff;padding:14px 18px;font-weight:700;font-size:21px;">
                            <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                                <tr>
                                    <td style="font-size:22px;font-weight:700;">PERINGATAN JATUH TEMPO: H-{H_MINUS}</td>
                                    <td align="right" style="font-size:20px;font-weight:700;">{{request.ReminderTime}}</td>
                                </tr>
                            </table>
                        </td>
                    </tr>
                    <tr>
                        <td style="padding:34px 30px 18px 30px;">
                            <h1 style="margin:0 0 10px 0;text-align:center;font-size:46px;line-height:1.2;color:#2c3e50;">Dashboard CFO</h1>
                            <p style="margin:0 0 28px 0;text-align:center;font-size:31px;color:#9aa4af;">Sistem Otomatisasi Pembayaran (Bunga/Prioritas)</p>
                            <p style="margin:0 0 22px 0;font-size:34px;line-height:1.4;">Yth. Tim Finance &amp; Direksi,</p>
                            <p style="margin:0 0 26px 0;font-size:38px;line-height:1.5;">Kami menginformasikan bahwa terdapat dokumen tagihan masuk dari perbankan yang akan segera jatuh tempo (H-{H_MINUS}). Mohon periksa lampiran PDF di bawah ini untuk rincian lebih lanjut.</p>

                            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border:1px solid #e1e5ea;border-radius:6px;overflow:hidden;">
                                <tr>
                                    <td style="padding:16px 18px;font-size:31px;color:#6b7280;width:40%;border-bottom:1px solid #e9edf2;">Jenis Tagihan:</td>
                                    <td style="padding:16px 18px;font-size:32px;font-weight:700;border-bottom:1px solid #e9edf2;color:#2d3a4a;">{{request.JenisTagihan}}</td>
                                </tr>
                                <tr>
                                    <td style="padding:16px 18px;font-size:31px;color:#6b7280;width:40%;border-bottom:1px solid #e9edf2;">Bank Terkait:</td>
                                    <td style="padding:16px 18px;font-size:32px;font-weight:700;border-bottom:1px solid #e9edf2;color:#2d3a4a;">{{request.BankName}}</td>
                                </tr>
                                <tr>
                                    <td style="padding:16px 18px;font-size:31px;color:#6b7280;width:40%;border-bottom:1px solid #e9edf2;">Jatuh Tempo:</td>
                                    <td style="padding:16px 18px;font-size:32px;font-weight:700;border-bottom:1px solid #e9edf2;color:#d9534f;">{{request.TanggalJatuhTempo}}</td>
                                </tr>
                                <tr>
                                    <td style="padding:16px 18px;font-size:31px;color:#6b7280;width:40%;">Total Tagihan:</td>
                                    <td style="padding:16px 18px;font-size:37px;font-weight:800;color:#2d3a4a;">Rp {{request.Nominal}}</td>
                                </tr>
                            </table>

                            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:30px;border:2px dashed #d3d8df;border-radius:8px;background:#fafbfc;">
                                <tr>
                                    <td align="center" style="padding:36px 20px;">
                                        <div style="font-size:60px;line-height:1;margin-bottom:14px;">📄</div>
                                        <div style="font-size:35px;font-weight:800;color:#2d3a4a;">{{request.DocumentFileName}}</div>
                                        <div style="font-size:27px;color:#9aa4af;margin-top:8px;">Ukuran file: {{request.FileSize}}</div>
                                        <a href="{{request.DownloadUrl}}" style="display:inline-block;margin-top:22px;background:#e74c3c;color:#ffffff;text-decoration:none;padding:13px 30px;border-radius:6px;font-size:26px;font-weight:800;">DOWNLOAD PDF</a>
                                    </td>
                                </tr>
                            </table>

                            <p style="margin:28px 0 0 0;text-align:center;font-size:30px;color:#9aa4af;font-style:italic;">*Harap lakukan pengecekan saldo pada Cash of Account sebelum melakukan eksekusi pembayaran.</p>
                        </td>
                    </tr>
                    <tr>
                        <td style="padding:18px 24px;border-top:1px solid #eceff3;text-align:center;font-size:24px;color:#a4acb6;">Dokumen ini dihasilkan secara otomatis oleh Smart Garment Financial System.</td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>
""";
        }

        private static string BuildDefaultPlainText(SendTestEmailRequest request)
        {
                return string.Join(Environment.NewLine,
                        "Dashboard CFO - Peringatan Jatuh Tempo",
                        "",
                        $"Jenis Tagihan: {request.JenisTagihan}",
                        $"Bank Terkait: {request.BankName}",
                        $"Jatuh Tempo: {request.TanggalJatuhTempo}",
                        $"Total Tagihan: Rp {request.Nominal}",
                        $"Dokumen: {request.DocumentFileName} ({request.FileSize})",
                        $"Download: {request.DownloadUrl}");
        }

    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/notifications/overdue", async (
                ClaimsPrincipal user,
                AccurateHttpClient accurateClient,
                IAccurateService accurateService,
                ICompanyAccessService access,
                IMemoryCache cache,
                ILogger<OverdueNotificationDto> logger,
                CancellationToken ct) =>
            {
                // Resolve user identity for cache key
                var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
                var cacheKey = $"overdue-notifs:{userId}";

                // Check cache first
                if (cache.TryGetValue(cacheKey, out List<OverdueNotificationDto>? cachedResult) && cachedResult != null)
                {
                    logger.LogInformation("[Notifications] Cache hit for user={User}, count={Count}", userId, cachedResult.Count);
                    return Results.Json(new { s = true, notifications = cachedResult });
                }

                // Get all companies the user has access to
                var accessResult = await access.NormalizeAndAuthorizeAsync(
                    user,
                    Array.Empty<string>(),
                    ct);

                if (!accessResult.Success)
                    return Results.Json(new { error = accessResult.Error }, statusCode: accessResult.StatusCode);

                var keys = accessResult.AccurateCompanyKeys;
                var asOfDate = DateOnly.FromDateTime(DateTime.UtcNow);

                logger.LogInformation(
                    "[Notifications] Computing overdue notifications for user={User}, companies={Companies}",
                    userId,
                    string.Join(", ", keys));

                var allNotifications = new List<OverdueNotificationDto>();

                foreach (var companyKey in keys)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        // Piutang (AR) notifications
                        var arNotifs = await OverdueNotificationComputation.ComputePiutangForCompany(
                            companyKey, asOfDate, accurateClient, ct);
                        allNotifications.AddRange(arNotifs);

                        // Utang (AP) notifications
                        var apNotifs = await OverdueNotificationComputation.ComputeUtangForCompany(
                            companyKey, asOfDate, accurateService, ct);
                        allNotifications.AddRange(apNotifs);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex,
                            "[Notifications] Failed to compute notifications for company={Company}",
                            companyKey);
                        // Continue with other companies — don't fail the whole request
                    }
                }

                // Sort: highest severity first, then by entity name
                allNotifications = allNotifications
                    .OrderByDescending(n => n.AgingBucket)
                    .ThenBy(n => n.Type)
                    .ThenBy(n => n.EntityName)
                    .ToList();

                // Cache the result
                cache.Set(cacheKey, allNotifications, CacheTtl);

                logger.LogInformation(
                    "[Notifications] Computed {Count} overdue notifications for user={User}",
                    allNotifications.Count, userId);

                return Results.Json(new { s = true, notifications = allNotifications });
            })
            .RequireAuthorization()
            .WithTags("Notifications");

        app.MapPost("/api/notifications/email/test", async (
                SendTestEmailRequest request,
                IEmailService emailService,
                ILogger<SendTestEmailRequest> logger,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.ToEmail))
                    return Results.Json(new { error = "toEmail is required." }, statusCode: 400);

                try
                {
                    var htmlBody = request.UseTemplate
                        ? BuildDefaultEmailHtml(request)
                        : request.HtmlBody;

                    if (string.IsNullOrWhiteSpace(htmlBody))
                        return Results.Json(new { error = "htmlBody is required when useTemplate = false." }, statusCode: 400);

                    await emailService.SendAsync(new SendEmailRequest
                    {
                        ToEmail = request.ToEmail,
                        ToName = request.ToName,
                        Subject = request.Subject,
                        HtmlBody = htmlBody,
                        PlainTextBody = string.IsNullOrWhiteSpace(request.PlainTextBody)
                            ? BuildDefaultPlainText(request)
                            : request.PlainTextBody,
                    }, ct);

                    return Results.Json(new { s = true, message = "Email sent." });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send test email to {ToEmail}", request.ToEmail);
                    return Results.Json(new { error = ex.Message }, statusCode: 500);
                }
            })
            .RequireAuthorization()
            .WithTags("Notifications");

        return app;
    }
}
