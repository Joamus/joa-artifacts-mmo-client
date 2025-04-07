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

    /**
     * Errors to implement:
     404: Map not found
     486: Action in progress (not same as cooldown, hmm)
     490: Char already at destination
     498: Char not found
     499: Cooldown
    */
    public async Task<GetCharactersResponse> GetCharacters()
    {
        var response = await _apiService.PostAsync($"/accounts/{_accountName}/characters", null);

        var result = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<GetCharactersResponse>(result)!;
    }

    // public async Task<GetCharactersResponse> GetAchievments()
    // {
    //     var response = await _apiService.PostAsync($"/accounts/{_accountName}/achievments", null);

    //     var result = await response.Content.ReadAsStringAsync();

    //     return JsonSerializer.Deserialize<GetCharactersResponse>(result)!;
    // }
}
