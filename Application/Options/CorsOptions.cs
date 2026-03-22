namespace MyBackend.Application.Options;

public class CorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>When non-empty, only these origins are allowed (with credentials-safe policy).</summary>
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}
