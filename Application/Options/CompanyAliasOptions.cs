namespace MyBackend.Application.Options;

/// <summary>Maps DB-facing company name/code to the exact Accurate config company key.</summary>
public class CompanyAliasOptions
{
    public const string SectionName = "CompanyAlias";

    /// <summary>Key: company_name or company_code from DB (case-insensitive match). Value: Accurate company key.</summary>
    public Dictionary<string, string> Map { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
