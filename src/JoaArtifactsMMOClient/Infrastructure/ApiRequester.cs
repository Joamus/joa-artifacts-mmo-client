using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application;

namespace Infrastructure;

public class ApiRequester
{
    private readonly float _secondsBetweenRequests = 0.6f;

    private readonly int MAX_RETRIES = 3;

    private DateTime _lastRequest;

    private readonly string _token;

    private static readonly ILogger logger = LoggerFactory
        .Create(AppLogger.options)
        .CreateLogger<ApiRequester>();

    // Putting them here for global access
    public static JsonSerializerOptions getJsonOptions()
    {
        if (_jsonOptions is null)
        {
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            };
            _jsonOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)
            );
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

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(requestUri);
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(
                $"GET Request with uri \"{requestUri}\" timed out - terminating application. Exception: {ex.Message}"
            );
            Environment.Exit(1);
            throw; // Never reached, but satisfies compiler
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(
                $"GET Request with uri \"{requestUri}\" failed with HttpRequestException - terminating application. Exception: {ex.Message}"
            );
            Environment.Exit(1);
            throw; // Never reached, but satisfies compiler
        }

        if (response is not null && (int)response.StatusCode >= 500)
        {
            logger.LogError(
                $"GET Request with uri \"{requestUri}\" failed with 5xx error - status code {response.StatusCode} - terminating application"
            );
            Environment.Exit(1);
        }

        if (response is not null && (int)response.StatusCode >= 400)
        {
            logger.LogWarning(
                $"GET Request with uri \"{requestUri}\" failed - status code {response.StatusCode} - message: {response.Content}"
            );
        }

        return response!;
    }

    public async Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent? content)
    {
        await ThrottleRequest();

        HttpResponseMessage? response = null;

        try
        {
            for (var i = 0; i < MAX_RETRIES; i++)
            {
                response = await _httpClient.PostAsync(requestUri, content);

                if ((int)response.StatusCode == 499)
                {
                    await Task.Delay((int)((_secondsBetweenRequests + 1) * 1000 * (i + 1) * 10));
                }
                else
                {
                    break;
                }
            }
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(
                $"POST Request with uri \"{requestUri}\" timed out - terminating application. Exception: {ex.Message}"
            );
            Environment.Exit(1);
            throw; // Never reached, but satisfies compiler
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(
                $"POST Request with uri \"{requestUri}\" failed with HttpRequestException - terminating application. Exception: {ex.Message}"
            );
            Environment.Exit(1);
            throw; // Never reached, but satisfies compiler
        }

        if (response is not null && (int)response.StatusCode >= 500)
        {
            logger.LogError(
                $"POST Request with uri \"{requestUri}\" failed with 5xx error - status code {response.StatusCode} - terminating application"
            );
            Environment.Exit(1);
        }

        if (response is not null && (int)response.StatusCode >= 400)
        {
            logger.LogWarning(
                $"POST Request with uri \"{requestUri}\" failed - status code {response.StatusCode} - message: {await response.Content.ReadAsStringAsync()}"
            );
        }

        return response!;
    }

    public async Task<HttpResponseMessage> PutAsync(string requestUri, HttpContent? content)
    {
        await ThrottleRequest();

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PutAsync(requestUri, content);
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(
                $"PUT Request with uri \"{requestUri}\" timed out - terminating application. Exception: {ex.Message}"
            );
            Environment.Exit(1);
            throw; // Never reached, but satisfies compiler
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(
                $"PUT Request with uri \"{requestUri}\" failed with HttpRequestException - terminating application. Exception: {ex.Message}"
            );
            Environment.Exit(1);
            throw; // Never reached, but satisfies compiler
        }

        if (response is not null && (int)response.StatusCode >= 500)
        {
            logger.LogError(
                $"PUT Request with uri \"{requestUri}\" failed with 5xx error - status code {response.StatusCode} - terminating application"
            );
            Environment.Exit(1);
        }

        return response!;
    }

    public async Task<HttpResponseMessage> DeleteAsync(string requestUri)
    {
        await ThrottleRequest();

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.DeleteAsync(requestUri);
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(
                $"DELETE Request with uri \"{requestUri}\" timed out - terminating application. Exception: {ex.Message}"
            );
            Environment.Exit(1);
            throw; // Never reached, but satisfies compiler
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(
                $"DELETE Request with uri \"{requestUri}\" failed with HttpRequestException - terminating application. Exception: {ex.Message}"
            );
            Environment.Exit(1);
            throw; // Never reached, but satisfies compiler
        }

        if (response is not null && (int)response.StatusCode >= 500)
        {
            logger.LogError(
                $"DELETE Request with uri \"{requestUri}\" failed with 5xx error - status code {response.StatusCode} - terminating application"
            );
            Environment.Exit(1);
        }

        return response!;
    }
}
