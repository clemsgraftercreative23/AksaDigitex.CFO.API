
namespace MyBackend.Application.Services;

public interface IAccurateService
{
    Task<string> GetDatabaseHost();
    Task<string> GetCoaDetail(string no);
}
