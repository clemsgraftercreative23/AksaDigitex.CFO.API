
using MyBackend.Infrastructure.Clients;

namespace MyBackend.Application.Services;

public class AccurateService : IAccurateService
{
    private readonly AccurateHttpClient _client;

    public AccurateService(AccurateHttpClient client)
    {
        _client = client;
    }

    public async Task<object> GetDatabaseHost(string? company = null)
    {
        return await _client.GetDatabaseHost(company);
    }

    public async Task<object> GetCoaDetail(string no, string? company = null)
    {
        return await _client.GetCoaDetail(no, company);
    }

    public async Task<string> GetCoaDetailRaw(string no, string? company = null)
    {
        return await _client.GetCoaDetailRaw(no, company);
    }

    public IReadOnlyList<string> GetCompanyNames()
    {
        return _client.GetCompanyNames();
    }
}
