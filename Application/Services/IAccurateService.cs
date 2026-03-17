
namespace MyBackend.Application.Services;

public interface IAccurateService
{
    Task<object> GetDatabaseHost(string? company = null);
    Task<object> GetCoaDetail(string no, string? company = null);
    /// <summary>Raw JSON from Accurate (envelope { s, d }) so frontend gets exact shape.</summary>
    Task<string> GetCoaDetailRaw(string no, string? company = null);
    /// <summary>Returns list of configured company names (PT) for Accurate.</summary>
    IReadOnlyList<string> GetCompanyNames();
}
