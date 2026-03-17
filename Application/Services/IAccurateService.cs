
namespace MyBackend.Application.Services;

public interface IAccurateService
{
    Task<object> GetDatabaseHost();
    Task<object> GetCoaDetail(string no);
    /// <summary>Raw JSON from Accurate (envelope { s, d }) so frontend gets exact shape.</summary>
    Task<string> GetCoaDetailRaw(string no);
}
