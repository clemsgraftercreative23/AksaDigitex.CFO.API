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

        return null;
    }
}
