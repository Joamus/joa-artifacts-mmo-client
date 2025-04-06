using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Application.Character;
using Infrastructure;

namespace Application.Actions.Services;

public class CharacterActionService
{
    ApiService _apiService;

    public CharacterActionService(ApiService apiService)
    {
        _apiService = apiService;
    }

    public async Task Move(PlayerCharacter character)
    {
        String coordinates = JsonSerializer.Serialize(new { x = 0, y = 0 });
        StringContent content = new StringContent(coordinates, Encoding.UTF8, "application/json");

        var response = await _apiService.PostAsync($"/my/{character.Name}", content);

        var result = response.Content.ReadAsStringAsync();
    }
}

// private record MoveResult()
