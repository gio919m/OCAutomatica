using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace OCAutomatica.Api.Epicor;

public sealed class EpicorClient : IEpicorClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly EpicorOptions _options;

    public EpicorClient(HttpClient httpClient, IOptions<EpicorOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public Task<T?> GetAsync<T>(
        string company,
        string relativePath,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        var request = BuildRequest(HttpMethod.Get, company, relativePath, credentials);
        return SendAsync<T>(request, ct);
    }

    public Task<T?> PostAsync<T>(
        string company,
        string relativePath,
        object body,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        var request = BuildRequest(HttpMethod.Post, company, relativePath, credentials);
        request.Content = JsonContent.Create(body);
        return SendAsync<T>(request, ct);
    }

    private HttpRequestMessage BuildRequest(
        HttpMethod method,
        string company,
        string relativePath,
        EpicorCredentials credentials)
    {
        var url = $"{_options.BaseUrl}/api/v2/odata/{company}/{relativePath}";
        var request = new HttpRequestMessage(method, url);
        AddAuthHeaders(request, credentials);
        return request;
    }

    private void AddAuthHeaders(HttpRequestMessage request, EpicorCredentials credentials)
    {
        var token = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.Add("x-api-key", _options.ApiKey);
    }

    public Task<T?> InvokeFunctionAsync<T>(
        string company,
        string library,
        string function,
        object input,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        // Epicor Functions live under /api/v2/efx/ — a completely separate URL
        // segment from the OData BO/BAQ surface under /api/v2/odata/ used by
        // every other method in this class. Confirmed in the Kinetic REST
        // Services Guide v2's "Invoking Epicor Functions" section.
        var url = $"{_options.BaseUrl}/api/v2/efx/{company}/{library}/{function}/";
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        AddAuthHeaders(request, credentials);
        // Explicit options: JsonContent.Create's overload without an options
        // argument falls back to System.Net.Http.Json's Web defaults (camelCase
        // naming policy), but Epicor Functions expect the input's C# property
        // names verbatim (PascalCase), matching how the OData BO surface is called.
        request.Content = JsonContent.Create(input, options: JsonOptions);
        return SendAsync<T>(request, ct);
    }

    private async Task<T?> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw BuildException((int)response.StatusCode, body);
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return string.IsNullOrWhiteSpace(json)
            ? default
            : JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private static EpicorException BuildException(int statusCode, string body)
    {
        string? errorMessage = null;
        try
        {
            var parsed = JsonSerializer.Deserialize<EpicorErrorBody>(body, JsonOptions);
            errorMessage = parsed?.ErrorMessage;
        }
        catch (JsonException)
        {
            // Body was not Epicor's structured error shape; fall through with the raw text.
        }

        var reason = ClassifyReason(statusCode, errorMessage);
        var message = errorMessage ?? $"Epicor responded {statusCode}: {body}";
        return new EpicorException(statusCode, reason, message);
    }

    private static EpicorErrorReason ClassifyReason(int statusCode, string? errorMessage)
    {
        if (statusCode != 401 || errorMessage is null) return EpicorErrorReason.Other;

        if (errorMessage.StartsWith("Invalid username or password", StringComparison.OrdinalIgnoreCase))
            return EpicorErrorReason.InvalidCredentials;

        if (errorMessage.StartsWith("Invalid API Key", StringComparison.OrdinalIgnoreCase))
            return EpicorErrorReason.InvalidApiKey;

        if (errorMessage.StartsWith("Access denied", StringComparison.OrdinalIgnoreCase))
            return EpicorErrorReason.AccessDenied;

        return EpicorErrorReason.Other;
    }

    private sealed class EpicorErrorBody
    {
        public string? ErrorMessage { get; set; }
    }
}
