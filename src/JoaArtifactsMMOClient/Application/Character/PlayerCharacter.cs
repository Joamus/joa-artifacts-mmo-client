using System.Text;
using System.Text.Json;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Errors;
using Application.Jobs;
using Application.Records;
using Application.Services;
using Infrastructure;
using OneOf;
using OneOf.Types;

namespace Application.Character;

public class PlayerCharacter
{
    public static readonly int AMOUNT_OF_FOOD_TO_KEEP = 20;

    public static readonly int PREFERED_FOOD_LEVEL_DIFFERENCE = 5;

    // If on cooldown, but not expected, just wait 5 seconds
    public CharacterSchema Character { get; private set; }

    public CooldownSchema? Cooldown { get; private set; } = null;

    // Poor man's semaphor - make something sturdier
    private bool _busy { get; set; } = false;

    private const string MediaType = "application/json";
    public List<CharacterJob> Jobs { get; private set; } = [];

    public CharacterJob? CurrentJob { get; private set; }

    private readonly GameState _gameState;

    private readonly ApiRequester _apiRequester;

    public bool idle
    {
        get { return CurrentJob == null && _busy == false; }
    }

    public void SetBusy(bool busy)
    {
        _busy = true;
    }

    public void QueueJob(CharacterJob job, bool highestPriority = false)
    {
        _busy = true;
        if (highestPriority)
        {
            Jobs.Insert(0, job);
        }
        else
        {
            Jobs.Add(job);
        }
        _busy = false;
    }

    public void QueueJobsBefore(Guid jobId, List<CharacterJob> jobs)
    {
        _busy = true;
        // Handle if the job to insert before is the current job - move it back into the list.
        var indexOf = Jobs.FindIndex(job => job.Id.Equals(jobId));

        if (indexOf == -1)
        {
            if (CurrentJob is not null && CurrentJob.Id.Equals(jobId))
            {
                Jobs.Insert(0, CurrentJob);
                indexOf = 0;
                CurrentJob = null;
            }
            else
            {
                foreach (var job in jobs)
                {
                    QueueJob(job);
                }
                _busy = false;
                return;
            }
        }

        var insertAtIndex = indexOf;

        if (insertAtIndex > 0)
        {
            insertAtIndex = insertAtIndex - 1;
        }

        Jobs.InsertRange(insertAtIndex, jobs);
        _busy = false;
    }

    public void QueueJobsAfter(Guid jobId, List<CharacterJob> jobs)
    {
        _busy = true;
        var indexOf = Jobs.FindIndex(job => job.Id.Equals(jobId));

        if (indexOf == -1)
        {
            if (CurrentJob is not null && CurrentJob.Id.Equals(jobId))
            {
                indexOf = 0;
            }
            else
            {
                foreach (var job in jobs)
                {
                    QueueJob(job);
                }
                _busy = false;
                return;
            }
        }

        var insertAtIndex = indexOf;

        if (insertAtIndex > 0)
        {
            insertAtIndex = insertAtIndex + 1;
        }

        Jobs.InsertRange(insertAtIndex, jobs);
        _busy = false;
    }

    public void ClearJobs()
    {
        _busy = true;
        Jobs = [];
        if (CurrentJob is not null)
        {
            CurrentJob.Interrrupt();
        }
        _busy = false;
    }

    public async Task<OneOf<AppError, None>> RunJob()
    {
        _busy = true;
        if (CurrentJob is not null)
        {
            _busy = false;
            return new None();
        }

        if (Jobs.Count > 0)
        {
            CurrentJob = Jobs[0];
            Jobs.RemoveAt(0);

            var result = await CurrentJob.RunAsync();

            CurrentJob = null;

            _busy = false;
            return result;
        }

        _busy = false;
        return new None();
    }

    public PlayerCharacter(CharacterSchema characterSchema)
    {
        Character = characterSchema;
        _gameState = GameServiceProvider.GetInstance().GetService<GameState>()!;
        _apiRequester = GameServiceProvider.GetInstance().GetService<ApiRequester>()!;
    }

    public async Task WaitForCooldown()
    {
        if (Cooldown is not null)
        {
            double waitingTime = (Cooldown.Expiration - DateTime.UtcNow).TotalSeconds;
            if (waitingTime > 0)
            {
                await Task.Delay((int)((waitingTime + 2) * 1000));
            }
            else
            {
                // Just to be sure
                await Task.Delay(1 * 1000);
            }
        }
    }

    public async Task Move(int x, int y)
    {
        if (Character.X == x && Character.Y == y)
        {
            return;
        }
        await PreTaskHandler();

        var _body = JsonSerializer.Serialize(new { x = x, y = y });
        StringContent body = new StringContent(_body, Encoding.UTF8, MediaType);

        var response = await _apiRequester.PostAsync($"/my/{Character.Name}/action/move", body);

        var content = await response.Content.ReadAsStringAsync();

        var result = JsonSerializer.Deserialize<MoveResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;

        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    // Make proper error handling with response codes etc.
    public async Task<OneOf<AppError, FightResponse>> Fight()
    {
        await PreTaskHandler();

        var response = await _apiRequester.PostAsync($"/my/{Character.Name}/action/fight", null);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<FightResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;

        PostTaskHandler(result.Data.Cooldown, result.Data.Character);

        return result;
    }

    public async Task Rest()
    {
        if (Character.Hp == Character.MaxHp)
        {
            return;
        }
        await PreTaskHandler();

        var response = await _apiRequester.PostAsync($"/my/{Character.Name}/action/rest", null);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    public async Task<OneOf<AppError, GatherResponse>> Gather()
    {
        await PreTaskHandler();

        var response = await _apiRequester.PostAsync(
            $"/my/{Character.Name}/action/gathering",
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
            return new AppError($"Error occured while gathering content: {content}");
        }
    }

    public async Task Craft(string itemCode, int quantity)
    {
        await PreTaskHandler();

        string _body = JsonSerializer.Serialize(new { code = itemCode, quantity });
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");
        var response = await _apiRequester.PostAsync($"/my/{Character.Name}/action/crafting", body);

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
            $"/my/{Character.Name}/action/bank/deposit/gold",
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
            $"/my/{Character.Name}/action/bank/withdraw/gold",
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
            $"/my/{Character.Name}/action/bank/deposit",
            body
        );

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    public async Task<OneOf<AppError, None>> WithdrawBankItem(string itemCode, int quantity)
    {
        await PreTaskHandler();

        string _body = JsonSerializer.Serialize(new { code = itemCode, quantity });
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");
        var response = await _apiRequester.PostAsync(
            $"/my/{Character.Name}/action/bank/withdraw",
            body
        );

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterResponse>(
            content,
            ApiRequester.getJsonOptions()
        );
        if (result is null)
        {
            return new AppError($"Error occured while trying to withdraw item");
        }
        else
        {
            PostTaskHandler(result.Data.Cooldown, result.Data.Character);
            return new None();
        }
    }

    public async Task UseItem(string itemCode, int quantity)
    {
        await PreTaskHandler();

        string _body = JsonSerializer.Serialize(new { code = itemCode, quantity });
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");
        var response = await _apiRequester.PostAsync($"/my/{Character.Name}/action/use", body);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    public async Task EquipItem(string itemCode, string slot, int quantity)
    {
        await PreTaskHandler();

        string _body = JsonSerializer.Serialize(
            new
            {
                code = itemCode,
                slot,
                quantity,
            }
        );
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");
        var response = await _apiRequester.PostAsync($"/my/{Character.Name}/action/equip", body);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    public async Task UnequipItem(string slot, int quantity)
    {
        await PreTaskHandler();

        string _body = JsonSerializer.Serialize(new { slot, quantity });
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");
        var response = await _apiRequester.PostAsync($"/my/{Character.Name}/action/equip", body);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    public async Task Recycle(string itemCode, int quantity)
    {
        await PreTaskHandler();

        string _body = JsonSerializer.Serialize(new { code = itemCode, quantity });
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");
        var response = await _apiRequester.PostAsync(
            $"/my/{Character.Name}/action/recycling",
            body
        );

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    public async Task TaskNew()
    {
        await PreTaskHandler();

        var response = await _apiRequester.PostAsync($"/my/{Character.Name}/action/task/new", null);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    public async Task TaskTrade(string itemCode, int quantity)
    {
        await PreTaskHandler();

        string _body = JsonSerializer.Serialize(new { code = itemCode, quantity });
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");
        var response = await _apiRequester.PostAsync(
            $"/my/{Character.Name}/action/task/trade",
            body
        );

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    public async Task TaskExchange()
    {
        await PreTaskHandler();

        var response = await _apiRequester.PostAsync(
            $"/my/{Character.Name}/action/task/exchange",
            null
        );

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    public async Task TaskComplete()
    {
        await PreTaskHandler();

        var response = await _apiRequester.PostAsync(
            $"/my/{Character.Name}/action/task/complete",
            null
        );

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    public async Task TaskCancel()
    {
        await PreTaskHandler();

        var response = await _apiRequester.PostAsync(
            $"/my/{Character.Name}/action/task/cancel",
            null
        );

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    public async Task DeleteItem(string itemCode, int quantity)
    {
        await PreTaskHandler();

        string _body = JsonSerializer.Serialize(new { code = itemCode, quantity });
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");
        var response = await _apiRequester.PostAsync($"/my/{Character.Name}/action/delete", body);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    public async Task PreTaskHandler()
    {
        await WaitForCooldown();
    }

    public void PostTaskHandler(CooldownSchema? cooldown, CharacterSchema? character)
    {
        if (cooldown is not null)
        {
            Cooldown = cooldown;
        }
        if (character is not null)
        {
            Character = character;
        }
    }

    public async Task<OneOf<AppError, None>> NavigateTo(string code, ContentType contentType)
    {
        // We don't know what it is, but it might be an item we wish to get

        if (contentType == ContentType.Resource)
        {
            var resources = _gameState.Resources.FindAll(resource =>
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
                throw new Exception($"Could not find map with resource {code}");
            }

            code = bestResource.Code;
        }

        var maps = _gameState.Maps.FindAll(map =>
            map.Content is not null && map.Content.Code == code
        );

        if (maps.Count == 0)
        {
            // TODO: Better handling
            throw new Exception($"Could not find map with code {code}");
        }

        MapSchema? closestMap = null;
        int closestCost = 0;

        foreach (var map in maps)
        {
            if (closestMap is null)
            {
                closestMap = map;
                closestCost = CalculationService.CalculateDistanceToMap(
                    Character.X,
                    Character.Y,
                    map.X,
                    map.Y
                );
                continue;
            }

            int cost = CalculationService.CalculateDistanceToMap(
                Character.X,
                Character.Y,
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
            return new AppError("Could not find closest map", ErrorStatus.NotFound);
        }

        await Move(closestMap.X, closestMap.Y);

        return new None();
    }

    public InventorySlot? GetItemFromInventory(string code)
    {
        return Character.Inventory.FirstOrDefault(item => item.Code == code);
    }

    public List<ItemInInventory> GetItemsFromInventoryWithType(string type)
    {
        List<ItemInInventory> items = [];

        foreach (var item in Character.Inventory)
        {
            var matchingItem = _gameState.Items.FirstOrDefault(_item => _item.Code == item.Code);

            // If item is null, then it has been deleted from the game or something
            if (matchingItem is not null && matchingItem.Type == type)
            {
                items.Add(new ItemInInventory { Item = matchingItem, Quantity = item.Quantity });
            }
        }

        return items;
    }

    public List<ItemInInventory> GetItemsFromInventoryWithSubtype(string subtype)
    {
        List<ItemInInventory> items = [];

        foreach (var item in Character.Inventory)
        {
            var matchingItem = _gameState.Items.FirstOrDefault(_item => _item.Code == item.Code);

            // If item is null, then it has been deleted from the game or something
            if (matchingItem is not null && matchingItem.Subtype == subtype)
            {
                items.Add(new ItemInInventory { Item = matchingItem, Quantity = item.Quantity });
            }
        }

        return items;
    }

    public int GetInventorySpaceUsed()
    {
        int inventorySpaceUsed = 0;
        foreach (var item in Character.Inventory)
        {
            inventorySpaceUsed += item.Quantity;
        }

        return inventorySpaceUsed;
    }

    public int GetInventorySpaceLeft()
    {
        int inventorySpaceUsed = GetInventorySpaceUsed();

        return Character.InventoryMaxItems - inventorySpaceUsed;
    }

    public OneOf<InventorySlot, AppError> GetItemSlot(string slot)
    {
        var prop = Character.GetType().GetProperty(slot);
        if (prop is null || prop.PropertyType != typeof(string))
        {
            return new AppError($"Invalid slot {slot}");
        }

        string? value = (string?)prop.GetValue(this);

        if (value is null)
        {
            return new AppError($"Invalid value in slot {slot} - {value}");
        }
        else if (value == "")
        {
            return new InventorySlot
            {
                Slot = slot,
                Code = "",
                Quantity = 1,
            };
        }

        if (slot == "Utility1Slot")
        {
            int quantity = Character.Utility1SlotQuantity;
            return new InventorySlot
            {
                Slot = slot,
                Code = value,
                Quantity = quantity,
            };
        }
        else if (slot == "Utility2Slot")
        {
            int quantity = Character.Utility2SlotQuantity;

            return new InventorySlot
            {
                Slot = slot,
                Code = value,
                Quantity = quantity,
            };
        }
        else
        {
            return new InventorySlot
            {
                Slot = slot,
                Code = value,
                Quantity = 1,
            };
        }
    }
}
