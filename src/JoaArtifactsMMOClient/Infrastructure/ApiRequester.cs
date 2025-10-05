using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infrastructure;

public class ApiRequester
{
    private readonly float _secondsBetweenRequests = 0.6f;

    private readonly int MAX_RETRIES = 3;

    private DateTime _lastRequest;

    private readonly string _token;

    // Putting them here for global access
    public static JsonSerializerOptions getJsonOptions()
    {
        if (_jsonOptions is null)
        {
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            };
            _jsonOptions.Converters.Add(new JsonStringEnumConverter { });
        }

        return _jsonOptions;
    }

    private static JsonSerializerOptions _jsonOptions;

    private HttpClient _httpClient { get; set; }

    public ApiRequester(string token)
    {
        _token = token;
        _lastRequest = DateTime.UtcNow;

        var handler = new HttpClientHandler() { MaxConnectionsPerServer = 10, UseProxy = false };

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.artifactsmmo.com"),
            Timeout = TimeSpan.FromSeconds(30),
        };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _token
        );
    }

    private async Task ThrottleRequest()
    {
        DateTime now = DateTime.UtcNow;
        double secondsDiff = (now - _lastRequest).TotalSeconds;
        if (secondsDiff < _secondsBetweenRequests)
        {
            await Task.Delay((int)((_secondsBetweenRequests + 1) * 1000));
        }
        _lastRequest = DateTime.UtcNow;
    }

    public async Task<HttpResponseMessage> GetAsync(string requestUri)
    {
        await ThrottleRequest();
        return await _httpClient.GetAsync(requestUri);
    }

    public async Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent? content)
    {
        await ThrottleRequest();

        HttpResponseMessage? response = null;

        for (var i = 0; i < MAX_RETRIES; i++)
        {
            response = await _httpClient.PostAsync(requestUri, content);

            if ((int)response.StatusCode == 499)
            {
                await Task.Delay((int)((_secondsBetweenRequests + 1) * 1000 * (i + 1) * 10));
            }
            else
            {
                return response;
            }
        }

        return response!;
    }

    public async Task<HttpResponseMessage> PutAsync(string requestUri, HttpContent? content)
    {
        await ThrottleRequest();
        return await _httpClient.PutAsync(requestUri, content);
    }

    public async Task<HttpResponseMessage> DeleteAsync(string requestUri)
    {
        await ThrottleRequest();
        return await _httpClient.DeleteAsync(requestUri);
    }
}
