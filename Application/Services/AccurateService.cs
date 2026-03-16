
using MyBackend.Infrastructure.Clients;

namespace MyBackend.Application.Services;

public class AccurateService : IAccurateService
{
    private readonly AccurateHttpClient _client;

    public AccurateService(AccurateHttpClient client)
    {
        _client = client;
    }

    public async Task<object> GetDatabaseHost()
    {
        return await _client.GetDatabaseHost();
    }

    public async Task<object> GetCoaDetail(string no)
    {
        return await _client.GetCoaDetail(no);
    }
}
