
namespace MyBackend.Application.Services;

public interface IAccurateService
{
    Task<object> GetDatabaseHost(string? company = null);
    Task<object> GetCoaDetail(string no, string? company = null);
    /// <summary>Raw JSON from Accurate (envelope { s, d }) so frontend gets exact shape.</summary>
    Task<string> GetCoaDetailRaw(string no, string? company = null);
    /// <summary>Returns list of configured company names (PT) for Accurate.</summary>
    IReadOnlyList<string> GetCompanyNames();
    /// <summary>Laba Rugi (P&amp;L) report. fromDate/toDate in dd/MM/yyyy. Returns raw JSON from Accurate.</summary>
    Task<string> GetPlAccountAmountRaw(string fromDate, string toDate, string? company = null);
    /// <summary>Neraca (Balance Sheet) report. asOfDate in dd/MM/yyyy. Returns raw JSON from Accurate.</summary>
    Task<string> GetBsAccountAmountRaw(string asOfDate, string? company = null);
    Task<string> GetOtherDepositListRaw(string? company = null);
    Task<string> GetOtherDepositDetailRaw(string id, string? company = null);
    Task<string> GetOtherPaymentListRaw(string? company = null);
    Task<string> GetOtherPaymentDetailRaw(string id, string? company = null);
    Task<string> GetPurchaseInvoiceListRaw(string? company = null);
    Task<string> GetPurchaseInvoiceDetailRaw(string id, string? company = null);
    Task<string> GetVendorDetailRaw(string id, string? company = null);
}
