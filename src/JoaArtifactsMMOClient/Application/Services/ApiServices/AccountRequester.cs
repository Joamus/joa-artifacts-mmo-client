using System.Text;
using System.Text.Json;
using Application.ArtifactsAPI.Responses;
using Application.Character;
using Infrastructure;

namespace Application.Services.ApiServices;

public class AccountRequester
{
    ApiRequester _apiService;
    readonly string _accountName;

    public AccountRequester(ApiRequester apiService, string accountName)
    {
        _apiService = apiService;
        _accountName = accountName;
    }

    public async Task<CharactersResponse> GetCharacters()
    {
        var response = await _apiService.PostAsync($"/accounts/{_accountName}/characters", null);

        var result = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<CharactersResponse>(result)!;
    }

    public async Task<CharactersResponse> GetItems()
    {
        var response = await _apiService.PostAsync($"/items", null);

        var result = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<CharactersResponse>(result)!;
    }

    public async Task<CharactersResponse> GetResources()
    {
        var response = await _apiService.PostAsync($"/resources", null);

        var result = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<CharactersResponse>(result)!;
    }

    public async Task<CharactersResponse> GetNpcs()
    {
        var response = await _apiService.PostAsync($"/npcs", null);

        var result = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<CharactersResponse>(result)!;
    }

    public async Task<CharactersResponse> GetMonsters()
    {
        var response = await _apiService.PostAsync($"/accounts/{_accountName}/characters", null);

        var result = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<CharactersResponse>(result)!;
    }

    public async Task<CharactersResponse> GetMaps()
    {
        var response = await _apiService.PostAsync($"/maps", null);

        var result = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<CharactersResponse>(result)!;
    }

    // public async Task<GetCharactersResponse> GetAchievements()
    // {
    //     var response = await _apiService.PostAsync($"/accounts/{_accountName}/achievments", null);

    //     var result = await response.Content.ReadAsStringAsync();

    //     return JsonSerializer.Deserialize<GetCharactersResponse>(result)!;
    // }
}
