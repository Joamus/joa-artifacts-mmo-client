using System.Net.Http.Headers;

namespace Infrastructure;

public class ApiService
{
    private readonly float _secondsBetweenRequests = 0.6f;

    private DateTime _lastRequest;

    private readonly string _token;

    private readonly HttpClient _httpClient;

    public ApiService(string token)
    {
        _token = token;
        _lastRequest = DateTime.UtcNow;

        _httpClient = new HttpClient() { BaseAddress = new Uri("https://api.artifactsmmo.com") };
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
            await System.Threading.Tasks.Task.Delay((int)(_secondsBetweenRequests * 1000));
        }
    }

    public async Task<HttpResponseMessage> GetAsync(string requestUri)
    {
        await ThrottleRequest();
        return await _httpClient.GetAsync(requestUri);
    }

    public async Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content)
    {
        await ThrottleRequest();
        return await _httpClient.PostAsync(requestUri, content);
    }

    public async Task<HttpResponseMessage> PutAsync(string requestUri, HttpContent content)
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
