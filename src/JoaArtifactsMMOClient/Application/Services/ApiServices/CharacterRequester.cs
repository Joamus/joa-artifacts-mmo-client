using System.Text;
using System.Text.Json;
using Application.ArtifactsAPI.Responses;
using Application.Character;
using Infrastructure;

namespace Application.Services.ApiServices;

public class CharacterRequester
{
    ApiRequester _apiService;

    public CharacterRequester(ApiRequester apiService)
    {
        _apiService = apiService;
    }

    /**
     * Errors to implement:
     404: Map not found
     486: Action in progress (not same as cooldown, hmm)
     490: Char already at destination
     498: Char not found
     499: Cooldown
    */
    public async Task<MoveResponse> Move(PlayerCharacter character)
    {
        var _body = JsonSerializer.Serialize(new { x = 0, y = 0 });
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");

        var response = await _apiService.PostAsync($"/my/{character.Name}/action/move", body);

        var result = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<MoveResponse>(result)!;
    }

    public async Task<FightResponse> Fight(PlayerCharacter character)
    {
        var response = await _apiService.PostAsync($"/my/{character.Name}/action/fight", null);

        var result = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<FightResponse>(result)!;
    }

    public async Task<GenericCharacterResponse> Rest(PlayerCharacter character)
    {
        var response = await _apiService.PostAsync($"/my/{character.Name}/action/rest", null);

        var result = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<GenericCharacterResponse>(result)!;
    }

    public async Task<FightResponse> Gather(PlayerCharacter character)
    {
        var response = await _apiService.PostAsync($"/my/{character.Name}/action/gather", null);

        var result = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<FightResponse>(result)!;
    }

    public async Task<FightResponse> Craft(PlayerCharacter character, string itemCode, int quantity)
    {
        string _body = JsonSerializer.Serialize(new { code = itemCode, quantity });
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");
        var response = await _apiService.PostAsync($"/my/{character.Name}/action/crafting", body);

        var result = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<FightResponse>(result)!;
    }

    public async Task<BankGoldTransactionResponse> DepositBankGold(
        PlayerCharacter character,
        int quantity
    )
    {
        string _body = JsonSerializer.Serialize(new { quantity });
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");
        var response = await _apiService.PostAsync(
            $"/my/{character.Name}/action/bank/deposit/gold",
            body
        );

        var result = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<BankGoldTransactionResponse>(result)!;
    }

    public async Task<BankGoldTransactionResponse> WithdrawBankGold(
        PlayerCharacter character,
        int quantity
    )
    {
        string _body = JsonSerializer.Serialize(new { quantity });
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");
        var response = await _apiService.PostAsync(
            $"/my/{character.Name}/action/bank/withdraw/gold",
            body
        );

        var result = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<BankGoldTransactionResponse>(result)!;
    }

    public async Task<GenericCharacterResponse> DepositBankItem(
        PlayerCharacter character,
        string itemCode,
        int quantity
    )
    {
        string _body = JsonSerializer.Serialize(new { code = itemCode, quantity });
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");
        var response = await _apiService.PostAsync(
            $"/my/{character.Name}/action/bank/deposit",
            body
        );

        var result = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<GenericCharacterResponse>(result)!;
    }

    public async Task<GenericCharacterResponse> WithdrawBankItem(
        PlayerCharacter character,
        string itemCode,
        int quantity
    )
    {
        string _body = JsonSerializer.Serialize(new { code = itemCode, quantity });
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");
        var response = await _apiService.PostAsync(
            $"/my/{character.Name}/action/bank/withdraw",
            body
        );

        var result = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<GenericCharacterResponse>(result)!;
    }
}

// private record MoveResult()
