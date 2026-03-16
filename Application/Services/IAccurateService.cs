
namespace MyBackend.Application.Services;

public interface IAccurateService
{
    Task<object> GetDatabaseHost();
    Task<object> GetCoaDetail(string no);
}
