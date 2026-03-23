using Microsoft.Extensions.Options;
using MyBackend.Application.Options;

namespace MyBackend.Application.Services;

public class AccurateCompanyKeyResolver : IAccurateCompanyKeyResolver
{
    private readonly CompanyAliasOptions _aliases;

    public AccurateCompanyKeyResolver(IOptions<CompanyAliasOptions> options)
    {
        _aliases = options.Value;
    }

    public string? ResolveToAccurateKey(string? userInput, IReadOnlyList<string> accurateCompanyKeys)
    {
        if (string.IsNullOrWhiteSpace(userInput)) return null;
        var trimmed = userInput.Trim();

        foreach (var key in accurateCompanyKeys)
        {
            if (string.Equals(key, trimmed, StringComparison.OrdinalIgnoreCase))
                return key;
        }

        foreach (var (from, to) in _aliases.Map)
        {
            if (!string.Equals(from, trimmed, StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var key in accurateCompanyKeys)
            {
                if (string.Equals(key, to, StringComparison.OrdinalIgnoreCase))
                    return key;
            }
        }

        // DB company_name often omits legal prefixes (e.g. "WONG HANG…") while Accurate:Companies keys
        // use full names (e.g. "PT WONG HANG BERSAUDARA"). Match after normalizing prefixes.
        var n = NormalizeLegalName(trimmed);
        if (n.Length > 0)
        {
            foreach (var key in accurateCompanyKeys)
            {
                if (string.Equals(NormalizeLegalName(key), n, StringComparison.OrdinalIgnoreCase))
                    return key;
            }
        }

        return null;
    }

    /// <summary>Strips common Indonesian entity prefixes so DB names align with Accurate config keys.</summary>
    private static string NormalizeLegalName(string s)
    {
        var t = s.Trim();
        var prefixes = new[]
        {
            "PT. ", "PT ", "CV. ", "CV ", "UD. ", "UD ", "PD. ", "PD ", "FIRMA ", "YAYASAN ",
        };
        foreach (var p in prefixes)
        {
            if (t.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            {
                t = t[p.Length..].Trim();
                break;
            }
        }

        while (t.Contains("  ", StringComparison.Ordinal))
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        return t;
    }
}
