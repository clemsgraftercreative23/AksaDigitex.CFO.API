namespace MyBackend.Application.Services;

public interface IAccurateCompanyKeyResolver
{
    /// <summary>
    /// Maps user input (DB name, code, or Accurate key) to the exact Accurate configuration company key.
    /// </summary>
    string? ResolveToAccurateKey(string? userInput, IReadOnlyList<string> accurateCompanyKeys);
}
