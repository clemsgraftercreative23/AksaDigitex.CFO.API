
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

    public async Task<string> GetOtherDepositListRaw(string? company = null)
    {
        return await _client.GetOtherDepositListRaw(company);
    }

    public async Task<string> GetOtherDepositDetailRaw(string id, string? company = null)
    {
        return await _client.GetOtherDepositDetailRaw(id, company);
    }

    public async Task<string> GetOtherPaymentListRaw(string? company = null)
    {
        return await _client.GetOtherPaymentListRaw(company);
    }

    public async Task<string> GetOtherPaymentDetailRaw(string id, string? company = null)
    {
        return await _client.GetOtherPaymentDetailRaw(id, company);
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
