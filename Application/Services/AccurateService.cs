
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

    public async Task<string> GetPlAccountAmountRaw(string fromDate, string toDate, string? company = null)
    {
        return await _client.GetPlAccountAmountRaw(fromDate, toDate, company);
    }

    public async Task<string> GetBsAccountAmountRaw(string asOfDate, string? company = null)
    {
        return await _client.GetBsAccountAmountRaw(asOfDate, company);
    }

    public async Task<string> GetSalesReceiptListRaw(string? company = null)
    {
        return await _client.GetSalesReceiptListRaw(company);
    }

    public async Task<string> GetSalesReceiptDetailRaw(string id, string? company = null)
    {
        return await _client.GetSalesReceiptDetailRaw(id, company);
    }

    public async Task<string> GetPurchaseInvoiceListRaw(string? company = null)
    {
        return await _client.GetPurchaseInvoiceListRaw(company);
    }

    public async Task<string> GetPurchaseInvoiceDetailRaw(string id, string? company = null)
    {
        return await _client.GetPurchaseInvoiceDetailRaw(id, company);
    }

    public async Task<string> GetVendorDetailRaw(string id, string? company = null)
    {
        return await _client.GetVendorDetailRaw(id, company);
    }
}
