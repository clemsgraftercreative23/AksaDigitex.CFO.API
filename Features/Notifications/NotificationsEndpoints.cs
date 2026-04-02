using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using MyBackend.Application.Services;
using MyBackend.Infrastructure.Clients;

namespace MyBackend.Features.Notifications;

public static class NotificationsEndpoints
{
    /// <summary>Cache TTL for overdue notifications (per-user).</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

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

        return app;
    }
}
