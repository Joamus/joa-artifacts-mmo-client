using System.Text.Json;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Infrastructure;

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

    public async Task<CharacterResponse> GetCharacter(string name)
    {
        var response = await _apiService.GetAsync($"/characters/{name}");

        var result = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<CharacterResponse>(
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
        var response = await _apiService.GetAsync(
            $"/maps?page={pageNumber}&hide_blocked_maps=true"
        );

        var result = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<MapsResponse>(result, ApiRequester.getJsonOptions())!;
    }

    public async Task<BankItemsResponse> GetBankItems()
    {
        int pageNumber = 1;
        bool doneFetching = false;

        List<DropSchema> items = [];

        while (!doneFetching)
        {
            var response = await _apiService.GetAsync($"/my/bank/items?page={pageNumber}");

            var result = await response.Content.ReadAsStringAsync();

            var content = JsonSerializer.Deserialize<BankItemsResponse>(
                result,
                ApiRequester.getJsonOptions()
            )!;

            if (content.Data.Count == 0)
            {
                doneFetching = true;
            }

            foreach (var item in content.Data)
            {
                items.Add(item);
            }

            pageNumber++;
        }

        return new BankItemsResponse { Data = items };
    }

    public async Task<List<TasksFullSchema>> GetTasks()
    {
        int pageNumber = 1;
        bool doneFetching = false;

        List<TasksFullSchema> tasks = [];

        while (!doneFetching)
        {
            var response = await _apiService.GetAsync($"/tasks/list?page={pageNumber}");

            var result = await response.Content.ReadAsStringAsync();

            var content = JsonSerializer.Deserialize<TasksListsResponse>(
                result,
                ApiRequester.getJsonOptions()
            )!;

            if (content.Data.Count == 0)
            {
                doneFetching = true;
            }

            foreach (var task in content.Data)
            {
                tasks.Add(task);
            }

            pageNumber++;
        }

        return tasks;
    }

    public async Task<BankDetailsResponse> GetBankDetails()
    {
        var response = await _apiService.GetAsync($"/my/bank");

        var result = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<BankDetailsResponse>(
            result,
            ApiRequester.getJsonOptions()
        )!;
    }

    public async Task<NpcItemsResponse> GetNpcItems(int pageNumber = 1)
    {
        var response = await _apiService.GetAsync($"/npcs/items?page={pageNumber}");

        var result = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<NpcItemsResponse>(result, ApiRequester.getJsonOptions())!;
    }

    public async Task<GetAccountAchievementsResponse> GetAccountAchievements(int pageNumber = 1)
    {
        var response = await _apiService.GetAsync(
            $"/accounts/{_accountName}/achievements?page={pageNumber}"
        );

        var result = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<GetAccountAchievementsResponse>(
            result,
            ApiRequester.getJsonOptions()
        )!;
    }

    public async Task<GetAchievementsResponse> GetAchievements()
    {
        var response = await _apiService.GetAsync($"/achievements");

        var result = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<GetAchievementsResponse>(
            result,
            ApiRequester.getJsonOptions()
        )!;
    }
}
