using System.Text.Json;
using Application.ArtifactsApi.Schemas.Responses;
using Infrastructure;
using Newtonsoft.Json.Serialization;

namespace Application.Services.ApiServices;

public class AccountRequester
{
    readonly ApiRequester _apiService;
    readonly string _accountName;

    public AccountRequester(ApiRequester apiRequester, string accountName)
    {
        _apiService = apiRequester;
        _accountName = accountName;
    }

    public async Task<CharactersResponse> GetCharacters()
    {
        var response = await _apiService.GetAsync($"/accounts/{_accountName}/characters");

        var result = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<CharactersResponse>(
            result,
            ApiRequester.getJsonOptions()
        )!;
    }

    public async Task<ItemsResponse> GetItems(int pageNumber = 1)
    {
        var response = await _apiService.GetAsync($"/items?page={pageNumber}");

        var result = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<ItemsResponse>(result, ApiRequester.getJsonOptions())!;
    }

    public async Task<ResourceResponse> GetResources(int pageNumber = 1)
    {
        var response = await _apiService.GetAsync($"/resources?page={pageNumber}");

        var result = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<ResourceResponse>(result, ApiRequester.getJsonOptions())!;
    }

    public async Task<NpcResponse> GetNpcs(int pageNumber = 1)
    {
        var response = await _apiService.GetAsync($"/npcs?page={pageNumber}");

        var result = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<NpcResponse>(result, ApiRequester.getJsonOptions())!;
    }

    public async Task<MonstersResponse> GetMonsters(int pageNumber = 1)
    {
        var response = await _apiService.GetAsync($"/monsters?page={pageNumber}");

        var result = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<MonstersResponse>(result, ApiRequester.getJsonOptions())!;
    }

    public async Task<MapsResponse> GetMaps(int pageNumber = 1)
    {
        var response = await _apiService.GetAsync($"/maps?page={pageNumber}");

        var result = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<MapsResponse>(result, ApiRequester.getJsonOptions())!;
    }

    // public async Task<GetCharactersResponse> GetAchievements()
    // [EnumMember(Value = "monster")]
    // [JsonStringEnumMemberName("monster")]
    // {
    //     var response = await _apiService.PostAsync($"/accounts/{_accountName}/achievments", null);

    //     var result = await response.Content.ReadAsStringAsync();

    //     return JsonSerializer.Deserialize<GetCharactersResponse>(result)!;
    // }
}
