
using System.Security.Cryptography;
using System.Text;

namespace MyBackend.Infrastructure.Clients;

public class AccurateHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;

    public AccurateHttpClient(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    public async Task<object> GetDatabaseHost()
    {
        var token = _config["Accurate:ApiToken"];
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

    public async Task<object> GetCoaDetail(string no)
    {
        var token = _config["Accurate:ApiToken"];
        var signatureKey = _config["Accurate:SignatureKey"];
        var host = _config["Accurate:Host"];

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var signature = GenerateSignature(signatureKey, timestamp);

        var url = $"{host}/accurate/api/glaccount/detail.do?no={no}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {token}");
        request.Headers.Add("X-Api-Timestamp", timestamp);
        request.Headers.Add("X-Api-Signature", signature);

        var response = await _httpClient.SendAsync(request);
        var jsonString = await response.Content.ReadAsStringAsync();
        return System.Text.Json.JsonSerializer.Deserialize<object>(jsonString);
    }

    private string GenerateSignature(string key, string timestamp)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp));
        return Convert.ToBase64String(hash);
    }
}
