using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using System.Text.Json;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Jobs;
using Infrastructure;
using Microsoft.Extensions.ObjectPool;
using Microsoft.OpenApi.Services;
using Microsoft.VisualBasic;
using OneOf;
using OneOf.Types;

namespace Application.Character;

public class PlayerCharacter
{
    public CharacterSchema _character { get; private set; }

    private CooldownSchema? _cooldown { get; set; } = null;

    private const string MediaType = "application/json";
    private List<CharacterJob> _jobs = [];

    private CharacterJob? _currentJob;

    private GameState _gameState;

    private readonly ApiRequester _apiRequester;

    public bool idle
    {
        get { return _currentJob == null; }
    }

    public void QueueJob(CharacterJob job, bool highestPriority = true)
    {
        if (highestPriority)
        {
            _jobs.Insert(0, job);
        }
        else
        {
            _jobs.Add(job);
        }
    }

    public void ClearJobs()
    {
        _jobs = [];
    }

    public async Task<OneOf<JobError, None>> RunJob()
    {
        if (_jobs.Count > 0)
        {
            _currentJob = _jobs[0];
            _jobs.RemoveAt(0);

            return await _currentJob.RunAsync();
        }
        else
        {
            _currentJob = null;
        }

        return new None();
    }

    /**
     * Can be used if we want to "interrupt" our current task, to work on a more important task.
     * E.g if processing a fight job, and our inventory almost is full, then we can make a DepositItems job higher priority,
     * and then resume our fight job afterwards.
    */
    private void MakeJobCurrentJob(CharacterJob characterJob)
    {
        var oldCurrentJob = _currentJob;

        if (oldCurrentJob is not null)
        {
            _jobs.Insert(0, oldCurrentJob);
        }
        _currentJob = null;
        _jobs.Insert(0, characterJob);
    }

    public PlayerCharacter(
        CharacterSchema characterSchema,
        GameState gameState,
        ApiRequester apiRequester
    )
    {
        _character = characterSchema;
        _gameState = gameState;
        _apiRequester = apiRequester;
    }

    public async Task WaitForCooldown()
    {
        if (_cooldown is not null)
        {
            double waitingTime = (_cooldown.Expiration - DateTime.UtcNow).TotalSeconds;
            if (waitingTime > 0)
            {
                await Task.Delay((int)((waitingTime + 1) * 1000));
            }
        }
    }

    public async Task Move(int x, int y)
    {
        if (_character.X == x && _character.Y == y)
        {
            return;
        }
        await PreTaskHandler();

        var _body = JsonSerializer.Serialize(new { x = x, y = y });
        StringContent body = new StringContent(_body, Encoding.UTF8, MediaType);

        var response = await _apiRequester.PostAsync($"/my/{_character.Name}/action/move", body);

        var content = await response.Content.ReadAsStringAsync();

        var result = JsonSerializer.Deserialize<MoveResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data?.Cooldown, result.Data?.Character);
    }

    // Make proper error handling with response codes etc.
    public async Task<OneOf<JobError, FightResponse>> Fight()
    {
        await PreTaskHandler();

        var response = await _apiRequester.PostAsync($"/my/{_character.Name}/action/fight", null);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<FightResponse>(
            content,
            ApiRequester.getJsonOptions()
        );

        if (result?.Data is not null)
        {
            PostTaskHandler(result.Data.Cooldown, result.Data.Character);
            return result;
        }
        else
        {
            return new JobError($"Error occured while fighting content: {content}");
        }
    }

    public async Task Rest()
    {
        if (_character.Hp == _character.MaxHp)
        {
            return;
        }
        await PreTaskHandler();

        var response = await _apiRequester.PostAsync($"/my/{_character.Name}/action/rest", null);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result?.Data.Cooldown, result?.Data.Character);
    }

    public async Task<OneOf<JobError, GatherResponse>> Gather()
    {
        await PreTaskHandler();

        var response = await _apiRequester.PostAsync(
            $"/my/{_character.Name}/action/gathering",
            null
        );

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GatherResponse>(
            content,
            ApiRequester.getJsonOptions()
        );
        if (result?.Data is not null)
        {
            PostTaskHandler(result.Data.Cooldown, result.Data.Character);
            return result;
        }
        else
        {
            return new JobError($"Error occured while gathering content: {content}");
        }
    }

    public async Task Craft(string itemCode, int quantity)
    {
        await PreTaskHandler();

        string _body = JsonSerializer.Serialize(new { code = itemCode, quantity });
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");
        var response = await _apiRequester.PostAsync(
            $"/my/{_character.Name}/action/crafting",
            body
        );

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<FightResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;

        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    public async Task DepositBankGold(int quantity)
    {
        await PreTaskHandler();

        string _body = JsonSerializer.Serialize(new { quantity });
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");
        var response = await _apiRequester.PostAsync(
            $"/my/{_character.Name}/action/bank/deposit/gold",
            body
        );

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BankGoldTransactionResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    public async Task WithdrawBankGold(int quantity)
    {
        await PreTaskHandler();

        string _body = JsonSerializer.Serialize(new { quantity });
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");
        var response = await _apiRequester.PostAsync(
            $"/my/{_character.Name}/action/bank/withdraw/gold",
            body
        );

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BankGoldTransactionResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    public async Task DepositBankItem(string itemCode, int quantity)
    {
        await PreTaskHandler();

        string _body = JsonSerializer.Serialize(new { code = itemCode, quantity });
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");
        var response = await _apiRequester.PostAsync(
            $"/my/{_character.Name}/action/bank/deposit",
            body
        );

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    public async Task WithdrawBankItem(string itemCode, int quantity)
    {
        await PreTaskHandler();

        string _body = JsonSerializer.Serialize(new { code = itemCode, quantity });
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");
        var response = await _apiRequester.PostAsync(
            $"/my/{_character.Name}/action/bank/withdraw",
            body
        );

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterSchema>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Cooldown, result.Character);
    }

    public async Task PreTaskHandler()
    {
        await WaitForCooldown();
    }

    public void PostTaskHandler(CooldownSchema? cooldown, CharacterSchema? character)
    {
        if (cooldown is not null)
        {
            _cooldown = cooldown;
        }
        if (character is not null)
        {
            _character = character;
        }
    }

    public async Task NavigateTo(string code, ContentType contentType)
    {
        // We don't know what it is, but it might be an item we wish to get

        if (contentType == ContentType.Resource)
        {
            var resources = _gameState._resources.FindAll(resource =>
                resource.Drops.Find(drop => drop.Code == code && drop.Rate > 0) != null
            );

            ResourceSchema? bestResource = null;
            int bestDropRate = 0;

            // The higher the drop rate, the lower the number. Drop rate of 1 means 100% chance, rate of 10 is 10% chance, rate of 100 is 1%

            foreach (var resource in resources)
            {
                if (bestDropRate == 0)
                {
                    bestResource = resource;
                    bestDropRate = resource.Drops[0].Rate;
                    continue;
                }
                var bestDrop = resource.Drops.Find(drop =>
                    drop.Code == code && drop.Rate < bestDropRate
                );

                if (bestDrop is not null)
                {
                    bestDropRate = bestDrop.Rate;
                    bestResource = resource;
                }
            }

            if (bestResource is null)
            {
                throw new Exception($"Could not find map with resource ${code}");
            }

            code = bestResource.Code;
        }

        var maps = _gameState._maps.FindAll(map =>
            map.Content is not null && map.Content.Code == code
        );

        if (maps.Count == 0)
        {
            // TODO: Better handling
            throw new Exception($"Could not find map with code ${code}");
        }

        MapSchema? closestMap = null;
        int closestCost = 0;

        foreach (var map in maps)
        {
            if (closestMap is null)
            {
                closestMap = map;
                closestCost = CalculationService.CalculateDistanceToMap(
                    this._character.X,
                    this._character.Y,
                    map.X,
                    map.Y
                );
                continue;
            }

            int cost = CalculationService.CalculateDistanceToMap(
                this._character.X,
                this._character.Y,
                map.X,
                map.Y
            );

            if (cost < closestCost)
            {
                closestMap = map;
                closestCost = cost;
            }

            // We are already standing on the map, we won't get any closer :-)
            if (cost == 0)
            {
                break;
            }
        }

        if (closestMap is null)
        {
            // TODO: Better handling
            throw new Exception("Could not find closest map");
        }

        await Move(closestMap.X, closestMap.Y);
    }

    public InventorySlot? GetItemFromInventory(string code)
    {
        return _character.Inventory.FirstOrDefault(item => item.Code == code);
    }
}
