using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyBackend.Application.Options;
using MyBackend.Features.Notifications;
using MyBackend.Infrastructure.Clients;

namespace MyBackend.Application.Services;

public sealed class OverdueUtangEmailWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<OverdueEmailScheduleOptions> _scheduleOptions;
    private readonly ILogger<OverdueUtangEmailWorker> _logger;

    public OverdueUtangEmailWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<OverdueEmailScheduleOptions> scheduleOptions,
        ILogger<OverdueUtangEmailWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _scheduleOptions = scheduleOptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _scheduleOptions.Value;
        if (!options.Enabled)
        {
            _logger.LogInformation("[OverdueUtangEmailWorker] Disabled by configuration.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nowLocal = GetLocalNow(options.TimeZoneId);
                var nextRunLocal = GetNextRun(nowLocal, options.HourLocal, options.MinuteLocal);
                var delay = nextRunLocal - nowLocal;

                _logger.LogInformation(
                    "[OverdueUtangEmailWorker] Next run at {NextRunLocal} ({DelayHours:F2}h from now)",
                    nextRunLocal,
                    delay.TotalHours);

                await Task.Delay(delay, stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[OverdueUtangEmailWorker] Unexpected worker error; retrying in 1 minute.");
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var accurateClient = scope.ServiceProvider.GetRequiredService<AccurateHttpClient>();
        var accurateService = scope.ServiceProvider.GetRequiredService<IAccurateService>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var emailOptions = scope.ServiceProvider.GetRequiredService<IOptions<EmailOptions>>().Value;
        var schedule = _scheduleOptions.Value;

        var companies = accurateClient.GetCompanyNames();
        if (companies.Count == 0)
        {
            _logger.LogWarning("[OverdueUtangEmailWorker] No Accurate companies configured. Skipping run.");
            return;
        }

        var recipients = schedule.Recipients
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (recipients.Count == 0 && !string.IsNullOrWhiteSpace(emailOptions.SenderEmail))
        {
            recipients.Add(emailOptions.SenderEmail);
        }

        if (recipients.Count == 0)
        {
            _logger.LogWarning("[OverdueUtangEmailWorker] No recipients configured. Set OverdueEmailSchedule:Recipients.");
            return;
        }

        var reminderDays = schedule.ReminderDaysBeforeDue
            .Where(x => x > 0)
            .Distinct()
            .OrderByDescending(x => x)
            .ToList();

        if (reminderDays.Count == 0)
        {
            _logger.LogWarning("[OverdueUtangEmailWorker] No reminder day configured. Set OverdueEmailSchedule:ReminderDaysBeforeDue.");
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var notifications = new List<OverdueNotificationDto>();

        foreach (var companyKey in companies)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var companyNotifs = await OverdueNotificationComputation.ComputeUtangDueSoonForCompany(
                    companyKey,
                    today,
                    accurateService,
                    reminderDays,
                    ct);

                notifications.AddRange(companyNotifs);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[OverdueUtangEmailWorker] Failed computing utang overdue for company={Company}",
                    companyKey);
            }
        }

        if (notifications.Count == 0)
        {
            _logger.LogInformation("[OverdueUtangEmailWorker] No utang reminder notifications matched configured H-minus days: {ReminderDays}.", string.Join(",", reminderDays));
            return;
        }

        notifications = notifications
            .OrderBy(x => x.DaysUntilDue ?? int.MaxValue)
            .ThenBy(x => x.EntityName)
            .ThenBy(x => x.InvoiceNumber)
            .ToList();

        foreach (var to in recipients)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var item in notifications)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await emailService.SendAsync(new SendEmailRequest
                    {
                        ToEmail = to,
                        Subject = OverdueUtangEmailTemplate.BuildSubject(item, item.DaysUntilDue),
                        HtmlBody = OverdueUtangEmailTemplate.BuildHtml(item, item.DaysUntilDue),
                        PlainTextBody = OverdueUtangEmailTemplate.BuildPlainText(item, item.DaysUntilDue),
                    }, ct);

                    _logger.LogInformation(
                        "[OverdueUtangEmailWorker] Sent due-soon utang email to {Recipient} for invoice={Invoice} hMinus={HMinus}",
                        to,
                        item.InvoiceNumber,
                        item.DaysUntilDue);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[OverdueUtangEmailWorker] Failed sending overdue utang email to {Recipient} for invoice={Invoice}",
                        to,
                        item.InvoiceNumber);
                }
            }
        }
    }

    private static DateTimeOffset GetLocalNow(string timeZoneId)
    {
        var utcNow = DateTimeOffset.UtcNow;
        var tz = ResolveTimeZone(timeZoneId);
        return TimeZoneInfo.ConvertTime(utcNow, tz);
    }

    private static DateTimeOffset GetNextRun(DateTimeOffset nowLocal, int hour, int minute)
    {
        var candidate = new DateTimeOffset(
            nowLocal.Year,
            nowLocal.Month,
            nowLocal.Day,
            hour,
            minute,
            0,
            nowLocal.Offset);

        if (candidate <= nowLocal)
        {
            candidate = candidate.AddDays(1);
        }

        return candidate;
    }

    private static TimeZoneInfo ResolveTimeZone(string configuredId)
    {
        var fallbacks = new[]
        {
            configuredId,
            "Asia/Jakarta",
            "SE Asia Standard Time",
        };

        foreach (var id in fallbacks.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch
            {
                // try next
            }
        }

        return TimeZoneInfo.Utc;
    }

}
