
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MyBackend.Infrastructure.Clients;

public class AccurateHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<AccurateHttpClient> _logger;

    public AccurateHttpClient(HttpClient httpClient, IConfiguration config, ILogger<AccurateHttpClient> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    private string GetToken(string? company = null)
    {
        if (!string.IsNullOrWhiteSpace(company))
        {
            var companyToken = _config[$"Accurate:Companies:{company}:ApiToken"];
            if (!string.IsNullOrEmpty(companyToken))
                return companyToken;
        }
        var defaultCompany = _config["Accurate:DefaultCompany"];
        if (!string.IsNullOrWhiteSpace(defaultCompany))
        {
            var defaultToken = _config[$"Accurate:Companies:{defaultCompany}:ApiToken"];
            if (!string.IsNullOrEmpty(defaultToken))
                return defaultToken;
        }
        return _config["Accurate:ApiToken"] ?? throw new InvalidOperationException("Accurate:ApiToken is not configured.");
    }

    public async Task<object> GetDatabaseHost(string? company = null)
    {
        var token = GetToken(company);
        var signatureKey = _config["Accurate:SignatureKey"];
        var url = $"{_config["Accurate:AccountUrl"]}/api/api-token.do";

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var signature = GenerateSignature(signatureKey, timestamp);

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Authorization", $"Bearer {token}");
        request.Headers.Add("X-Api-Timestamp", timestamp);
        request.Headers.Add("X-Api-Signature", signature);

        var response = await _httpClient.SendAsync(request);
        var jsonString = await response.Content.ReadAsStringAsync();
        return System.Text.Json.JsonSerializer.Deserialize<object>(jsonString);
    }

    private string GetHost(string? company = null)
    {
        if (!string.IsNullOrWhiteSpace(company))
        {
            var companyHost = _config[$"Accurate:Companies:{company}:Host"];
            if (!string.IsNullOrWhiteSpace(companyHost))
            {
                _logger.LogInformation("[Accurate] Company={Company} -> Host={Host} (company-specific)", company, companyHost);
                return companyHost;
            }
            _logger.LogInformation("[Accurate] Company={Company} -> Host kosong di config, pakai default", company);
        }
        var host = _config["Accurate:Host"];
        if (string.IsNullOrEmpty(host))
            throw new InvalidOperationException("Accurate:Host is not configured.");
        _logger.LogInformation("[Accurate] Company={Company} -> Host={Host} (default)", company ?? "(null)", host);
        return host;
    }

    /// <summary>
    /// Returns raw JSON string from Accurate so the envelope { "s", "d" } and balance are preserved for the frontend.
    /// </summary>
    public async Task<string> GetCoaDetailRaw(string no, string? company = null)
    {
        var token = GetToken(company);
        var signatureKey = _config["Accurate:SignatureKey"];
        var host = GetHost(company);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var signature = GenerateSignature(signatureKey, timestamp);

        var url = $"{host}/accurate/api/glaccount/detail.do?no={Uri.EscapeDataString(no)}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {token}");
        request.Headers.Add("X-Api-Timestamp", timestamp);
        request.Headers.Add("X-Api-Signature", signature);

        var response = await _httpClient.SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<object> GetCoaDetail(string no, string? company = null)
    {
        var jsonString = await GetCoaDetailRaw(no, company);
        return System.Text.Json.JsonSerializer.Deserialize<object>(jsonString);
    }

    /// <summary>
    /// Profit &amp; Loss (Laba Rugi) report from Accurate. Returns raw JSON.
    /// fromDate and toDate in format dd/MM/yyyy (e.g. 01/03/2026).
    /// </summary>
    public async Task<string> GetPlAccountAmountRaw(string fromDate, string toDate, string? company = null)
    {
        var token = GetToken(company);
        var signatureKey = _config["Accurate:SignatureKey"];
        var host = GetHost(company);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var signature = GenerateSignature(signatureKey, timestamp);

        var url = $"{host}/accurate/api/glaccount/get-pl-account-amount.do?fromDate={Uri.EscapeDataString(fromDate)}&toDate={Uri.EscapeDataString(toDate)}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {token}");
        request.Headers.Add("X-Api-Timestamp", timestamp);
        request.Headers.Add("X-Api-Signature", signature);

        var response = await _httpClient.SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Neraca (Balance Sheet) report from Accurate. Returns raw JSON.
    /// asOfDate in format dd/MM/yyyy (e.g. 17/03/2026).
    /// </summary>
    public async Task<string> GetBsAccountAmountRaw(string asOfDate, string? company = null)
    {
        var token = GetToken(company);
        var signatureKey = _config["Accurate:SignatureKey"];
        var host = GetHost(company);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var signature = GenerateSignature(signatureKey, timestamp);

        var url = $"{host}/accurate/api/glaccount/get-bs-account-amount.do?asOfDate={Uri.EscapeDataString(asOfDate)}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {token}");
        request.Headers.Add("X-Api-Timestamp", timestamp);
        request.Headers.Add("X-Api-Signature", signature);

        var response = await _httpClient.SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }

    public IReadOnlyList<string> GetCompanyNames()
    {
        var section = _config.GetSection("Accurate:Companies");
        if (!section.Exists())
            return Array.Empty<string>();
        return section.GetChildren().Select(c => c.Key).ToList();
    }

    private string GenerateSignature(string key, string timestamp)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp));
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Mengambil daftar Sales Order untuk Dashboard
    /// </summary>
    public async Task<string> GetSalesOrdersRaw(string? company = null)
    {
        // 1. Ambil Kunci & Token (Alfa & Bitren pakai host iris, yang lain pakai default)
        var token = GetToken(company);
        var signatureKey = _config["Accurate:SignatureKey"];
        var host = GetHost(company);

        _logger.LogInformation("[Accurate] GetSalesOrdersRaw company={Company} host={Host}", company ?? "(default)", host);
        Console.WriteLine($"GetSalesOrdersRaw company={company ?? "(default)"} host={host}");
        // 2. Bikin Gembok (Signature)
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var signature = GenerateSignature(signatureKey, timestamp);

        // 3. Tentukan URL dan Minta Kolom Spesifik
        // Kita minta: id, nomor SO, tanggal, nama pelanggan, entitas/cabang, total nilai, dan status.
        // Catatan: Nama field "branch" atau "outstandingAmount" mungkin perlu dicek lagi di response aslinya nanti.
        var url = $"{host}/accurate/api/sales-order/list.do?fields=id,number,transDate,customer,branch,totalAmount,status";

        // 4. Berangkatkan Kurir (Kirim Request)
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {token}");
        request.Headers.Add("X-Api-Timestamp", timestamp);
        request.Headers.Add("X-Api-Signature", signature);

        var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        // Log untuk debug: status, URL, dan response
        _logger.LogInformation("[Accurate] GetSalesOrdersRaw Response Status={StatusCode} URL={Url}", response.StatusCode, url);
        if (!response.IsSuccessStatusCode)
            _logger.LogWarning("[Accurate] GetSalesOrdersRaw Error Body (first 500 chars): {Body}", responseBody.Length > 500 ? responseBody[..500] : responseBody);
        else
            _logger.LogInformation("[Accurate] GetSalesOrdersRaw Body length={Len} preview={Preview}", responseBody.Length, responseBody.Length > 200 ? responseBody[..200] + "..." : responseBody);

        return responseBody;
    }
}
