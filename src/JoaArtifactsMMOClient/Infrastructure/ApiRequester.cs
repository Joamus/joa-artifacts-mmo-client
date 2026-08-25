using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Application;
using Application.Errors;

namespace Infrastructure;

public partial class ApiRequester
{
    [GeneratedRegex("\"[-+]?\\d+(\\.\\d+)?\"")]
    private static partial Regex CdFromResponseRegex();

    private readonly float _secondsBetweenRequests = 0.6f;
    static int AMOUNT_OF_500_REQUESTS = 0;

    private readonly int MAX_RETRIES = 3;

    private DateTime _lastRequest;

    private readonly string _token;

    private readonly FifoSemaphore ThrottleLock = new(1, 1);

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

    public ApiRequester(string token, bool beta)
    {
        _token = token;
        _lastRequest = DateTime.UtcNow;

        var handler = new HttpClientHandler() { MaxConnectionsPerServer = 10, UseProxy = false };

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(
                beta ? "https://api.beta.artifactsmmo.com" : "https://api.artifactsmmo.com"
            ),
            Timeout = TimeSpan.FromSeconds(60),
        };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _token
        );
    }

    private static async Task<string> ReadErrorContentAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            return $"<failed to read response content: {ex.Message}>";
        }
    }

    private async Task ThrottleRequest()
    {
        try
        {
            await ThrottleLock.WaitAsync();

            DateTime now = DateTime.UtcNow;
            double secondsDiff = (now - _lastRequest).TotalSeconds;

            if (secondsDiff <= _secondsBetweenRequests)
            {
                await Task.Delay((int)(_secondsBetweenRequests * 1000));
            }
            _lastRequest = DateTime.UtcNow;
        }
        finally
        {
            ThrottleLock.Release();
        }
    }

    public async Task<HttpResponseMessage> GetAsync(string requestUri)
    {
        await ThrottleRequest();

        HttpResponseMessage? response = null;

        try
        {
            for (var i = 0; i < MAX_RETRIES; i++)
            {
                response = await _httpClient.GetAsync(requestUri);

                int responseCode = (int)response.StatusCode;

                if (responseCode == 499 || responseCode == 429)
                {
                    await Task.Delay(1 * 1000);
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
            string responseContent = await ReadErrorContentAsync(response);
            string errorMessage =
                $"GET Request with uri \"{requestUri}\" failed with 5xx error - status code {response.StatusCode} - message: {responseContent} - terminating application";

            logger.LogError(errorMessage);

            AMOUNT_OF_500_REQUESTS += 1;

            if (AMOUNT_OF_500_REQUESTS >= 10)
            {
                logger.LogError(
                    $"Terminating application - {AMOUNT_OF_500_REQUESTS} x 500 requests reached"
                );
                Environment.Exit(1);
            }
            else
            {
                throw new Exception(errorMessage);
            }
        }

        if (response is not null && (int)response.StatusCode == 429)
        {
            var errorMessage = $"GET Request with uri \"{requestUri}\" failed due to rate limit";
            logger.LogError(errorMessage);
            throw new Exception(errorMessage);
        }

        if (response is not null && (int)response.StatusCode >= 400)
        {
            string responseContent = await ReadErrorContentAsync(response);
            logger.LogWarning(
                $"GET Request with uri \"{requestUri}\" failed - status code {response.StatusCode} - message: {responseContent}"
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
                string contentString = content is not null ? await content.ReadAsStringAsync() : "";
                logger.LogDebug($"POST \"{requestUri}\" - content: {contentString}");
                response = await _httpClient.PostAsync(requestUri, content);

                int statusCode = (int)response.StatusCode;
                if (statusCode == 499 || statusCode == 486)
                {
                    Regex regex = CdFromResponseRegex();

                    float secondsToWait = 1;

                    string? stringContent =
                        statusCode == 499 ? await response.Content.ReadAsStringAsync() ?? "" : "";

                    var parsedSeconds = ParseSecondFromCooldownResponse(stringContent);

                    if (parsedSeconds is not null)
                    {
                        // Function doesn't correctly parse decimal points, so just add one second
                        secondsToWait = (float)parsedSeconds + 1;
                    }

                    await Task.Delay((int)Math.Ceiling(secondsToWait) * 1000);
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
            string responseContent = await ReadErrorContentAsync(response);
            logger.LogError(
                $"POST Request with uri \"{requestUri}\" failed with 5xx error - status code {response.StatusCode} - message: {responseContent} - terminating application"
            );
            Environment.Exit(1);
        }

        if (response is not null && (int)response.StatusCode == 429)
        {
            var errorMessage = $"GET Request with uri \"{requestUri}\" failed due to rate limit";
            logger.LogError(errorMessage);
            throw new Exception(errorMessage);
        }

        if (response is not null && (int)response.StatusCode >= 400)
        {
            string responseContent = await ReadErrorContentAsync(response);
            logger.LogWarning(
                $"POST Request with uri \"{requestUri}\" failed - status code {response.StatusCode} - message: {responseContent}"
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

    static float? ParseSecondFromCooldownResponse(string content)
    {
        // Format: {"error":{"code":499,"message":"The character is in cooldown: 23.27 seconds remaining."}}
        var splitOne = content.Split("is in cooldown:");

        var splitTwo = splitOne.LastOrDefault()?.Split("seconds remaining");

        string secondsContent =
            (splitTwo?.FirstOrDefault() ?? "").Trim().Split(".").FirstOrDefault() ?? "";

        bool wasParsed = float.TryParse(secondsContent, out float parsedSeconds);

        return wasParsed ? parsedSeconds : null;
    }
}
