using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MyBackend.Application.Auth;
using MyBackend.Infrastructure.Persistence;

namespace MyBackend.Application.Services;

public class CompanyAccessService : ICompanyAccessService
{
    private readonly CfoDbContext _db;
    private readonly IAccurateService _accurate;
    private readonly IAccurateCompanyKeyResolver _resolver;
    private readonly IConfiguration _configuration;

    public CompanyAccessService(
        CfoDbContext db,
        IAccurateService accurate,
        IAccurateCompanyKeyResolver resolver,
        IConfiguration configuration)
    {
        _db = db;
        _accurate = accurate;
        _resolver = resolver;
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<string>> GetAllowedAccurateCompanyKeysAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var accurateKeys = _accurate.GetCompanyNames().ToList();
        if (accurateKeys.Count == 0)
            return Array.Empty<string>();

        // Super admin must see every entity under Accurate:Companies — not only rows that exist in CFO DB.
        if (user.IsInRole(AuthConstants.SuperDuperAdminRole))
            return accurateKeys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

        var isAll = IsAllCompany(user);

        if (isAll)
        {
            var activeCompanies = await _db.Companies
                .AsNoTracking()
                .Where(c => c.IsActive)
                .ToListAsync(cancellationToken);

            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in activeCompanies)
            {
                var k = _resolver.ResolveToAccurateKey(c.CompanyName, accurateKeys)
                    ?? (c.CompanyCode != null ? _resolver.ResolveToAccurateKey(c.CompanyCode, accurateKeys) : null);
                if (k != null)
                    set.Add(k);
            }

            if (set.Count == 0)
                return accurateKeys;

            return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        var companyIdClaim = user.FindFirst(AppClaims.CompanyId)?.Value;
        if (!int.TryParse(companyIdClaim, out var companyId))
            return Array.Empty<string>();

        var row = await _db.Companies.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);
        if (row == null)
            return Array.Empty<string>();

        var key = _resolver.ResolveToAccurateKey(row.CompanyName, accurateKeys)
            ?? (row.CompanyCode != null ? _resolver.ResolveToAccurateKey(row.CompanyCode, accurateKeys) : null);

        return key != null ? new[] { key } : Array.Empty<string>();
    }

    public async Task<CompanyAccessResult> NormalizeAndAuthorizeAsync(
        ClaimsPrincipal user,
        IReadOnlyList<string?>? requested,
        CancellationToken cancellationToken = default)
    {
        var allowed = await GetAllowedAccurateCompanyKeysAsync(user, cancellationToken);
        if (allowed.Count == 0)
            return CompanyAccessResult.Fail(403, "No Accurate company access is configured for this account.");

        var isAll = IsAllCompany(user);
        var nonEmptyRequested = (requested ?? Array.Empty<string?>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .ToList();

        if (nonEmptyRequested.Count == 0)
        {
            if (!isAll && allowed.Count >= 1)
                return CompanyAccessResult.Ok(new[] { allowed[0] });

            var defaultCompany = _configuration["Accurate:DefaultCompany"];
            var accurateKeys = _accurate.GetCompanyNames().ToList();
            if (!string.IsNullOrWhiteSpace(defaultCompany))
            {
                var defKey = _resolver.ResolveToAccurateKey(defaultCompany, accurateKeys);
                if (defKey != null && allowed.Any(a => string.Equals(a, defKey, StringComparison.OrdinalIgnoreCase)))
                    return CompanyAccessResult.Ok(new[] { allowed.First(a => string.Equals(a, defKey, StringComparison.OrdinalIgnoreCase)) });
            }

            if (allowed.Count == 1)
                return CompanyAccessResult.Ok(allowed);

            return CompanyAccessResult.Fail(400, "Query parameter company is required.");
        }

        var accurateKeysForResolve = _accurate.GetCompanyNames().ToList();
        var resolved = new List<string>();
        foreach (var raw in nonEmptyRequested)
        {
            var key = _resolver.ResolveToAccurateKey(raw, accurateKeysForResolve);
            if (key == null)
                return CompanyAccessResult.Fail(400, $"Unknown company: {raw}");

            if (!allowed.Any(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase)))
                return CompanyAccessResult.Fail(403, $"Access denied for company: {raw}");

            var canonical = allowed.First(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));
            if (!resolved.Contains(canonical, StringComparer.Ordinal))
                resolved.Add(canonical);
        }

        return CompanyAccessResult.Ok(resolved);
    }

    private static bool IsAllCompany(ClaimsPrincipal user)
    {
        var v = user.FindFirst(AppClaims.IsAllCompany)?.Value;
        return string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    }
}
