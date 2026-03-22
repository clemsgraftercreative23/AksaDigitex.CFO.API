using System.Security.Claims;

namespace MyBackend.Application.Services;

public interface ICompanyAccessService
{
    /// <summary>Accurate company keys the user may request (intersection with configured Accurate companies when applicable).</summary>
    Task<IReadOnlyList<string>> GetAllowedAccurateCompanyKeysAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates requested company names/keys and returns canonical Accurate keys.
    /// If <paramref name="requested"/> is null/empty and the user is scoped to one company, returns that single key.
    /// </summary>
    Task<CompanyAccessResult> NormalizeAndAuthorizeAsync(
        ClaimsPrincipal user,
        IReadOnlyList<string?>? requested,
        CancellationToken cancellationToken = default);
}

public sealed class CompanyAccessResult
{
    public bool Success { get; init; }
    public IReadOnlyList<string> AccurateCompanyKeys { get; init; } = Array.Empty<string>();
    public int StatusCode { get; init; }
    public string? Error { get; init; }

    public static CompanyAccessResult Ok(IReadOnlyList<string> keys) =>
        new() { Success = true, AccurateCompanyKeys = keys, StatusCode = 200 };

    public static CompanyAccessResult Fail(int statusCode, string message) =>
        new() { Success = false, StatusCode = statusCode, Error = message };
}
