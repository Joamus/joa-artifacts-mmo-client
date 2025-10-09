using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Requests;
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
    [JsonIgnore]
    public static readonly int MIN_AMOUNT_OF_FOOD_TO_KEEP = 20;

    [JsonIgnore]
    public static readonly int PREFERED_FOOD_LEVEL_DIFFERENCE = 5;

    // If on cooldown, but not expected, just wait 5 seconds
    public CharacterSchema Schema { get; private set; }

    public CooldownSchema? Cooldown { get; private set; } = null;

    // Poor man's semaphor - make something sturdier
    private bool Busy { get; set; } = false;

    [JsonIgnore]
    private const string MediaType = "application/json";
    public List<CharacterJob> Jobs { get; private set; } = [];

    public List<CharacterJob> IdleJobs { get; private set; } = [];

    public CharacterJob? CurrentJob { get; private set; }

    [JsonIgnore]
    private readonly GameState GameState;

    [JsonIgnore]
    private readonly ApiRequester ApiRequester;

    [JsonIgnore]
    public PlayerActionService PlayerActionService { get; init; }

    [JsonIgnore]
    public ILogger Logger { get; init; }

    public bool Idle
    {
        get { return CurrentJob is null && Busy == false && Suspended == false; }
    }

    public bool Suspended { get; private set; }

    public void Suspend(bool interrupt = true)
    {
        if (CurrentJob is not null && interrupt)
        {
            CurrentJob.Interrrupt();
        }

        Suspended = true;
    }

    public void Unsuspend()
    {
        Suspended = false;
    }

    public void QueueJob(CharacterJob job, bool highestPriority = false)
    {
        Busy = true;
        if (highestPriority)
        {
            Jobs.Insert(0, job);
        }
        else
        {
            Jobs.Add(job);
        }
        Busy = false;
    }

    public void AddIdleJob(CharacterJob job)
    {
        Busy = true;
        IdleJobs.Add(job);
        Busy = false;
    }

    public void QueueJobsBefore(Guid jobId, List<CharacterJob> jobs)
    {
        Busy = true;
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
                Busy = false;
                return;
            }
        }

        var insertAtIndex = indexOf;

        if (insertAtIndex > 0)
        {
            insertAtIndex = insertAtIndex - 1;
        }

        Jobs.InsertRange(insertAtIndex, jobs);
        Busy = false;
    }

    public void QueueJobsAfter(Guid jobId, List<CharacterJob> jobs)
    {
        Busy = true;
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
                Busy = false;
                return;
            }
        }

        var insertAtIndex = indexOf;

        if (insertAtIndex > 0)
        {
            insertAtIndex = insertAtIndex + 1;
        }

        Jobs.InsertRange(insertAtIndex, jobs);
        Busy = false;
    }

    public void DeleteJob(Guid id)
    {
        Busy = true;
        Jobs = Jobs.Where(job => !job.Id.Equals(id)).ToList();
        IdleJobs = IdleJobs.Where(job => !job.Id.Equals(id)).ToList();
        if (CurrentJob is not null && CurrentJob.Id.Equals(id))
        {
            CurrentJob.Interrrupt();
            CurrentJob = null;
        }
        Busy = false;
    }

    public void ClearJobs()
    {
        Busy = true;
        Jobs = [];
        if (CurrentJob is not null)
        {
            CurrentJob.Interrrupt();
            CurrentJob = null;
        }
        Busy = false;
    }

    public void ClearIdleJobs()
    {
        Busy = true;
        IdleJobs = [];
        if (
            CurrentJob is not null
            && IdleJobs.Find(job => job.Id.Equals(CurrentJob.Id)) is not null
        )
        {
            CurrentJob.Interrrupt();
            CurrentJob = null;
        }
        Busy = false;
    }

    public async Task<OneOf<AppError, None>> RunJob()
    {
        Busy = true;
        if (Suspended || CurrentJob is not null)
        {
            Busy = false;
            return new None();
        }

        Logger.LogInformation($"{GetType().Name}: [{Schema.Name}] run job start");

        CharacterJob? nextJob = null;

        if (Jobs.Count > 0)
        {
            nextJob = Jobs[0];
            Jobs.RemoveAt(0);
        }
        else if (IdleJobs.Count > 0)
        {
            int randomIndex = new Random().Next(0, IdleJobs.Count);

            CharacterJob randomJob = IdleJobs.ElementAtOrDefault(randomIndex)!;
            var clonedIdleJob = randomJob.Clone();

            // This is mess, but we want the job to have a reference to this character,
            // and not a clone. It's only the rest of the job we want to clone, in case the job
            // saves state on it, that should be reset. A better solution could be found :D
            // clonedIdleJob.Character = this;
            // clonedIdleJob.gameState = GameState;

            Logger.LogInformation(
                $"{GetType().Name}: [{Schema.Name}] picked random job index {randomIndex} of {IdleJobs.Count - 1}"
            );
            nextJob = clonedIdleJob;
        }

        CurrentJob = nextJob;

        if (CurrentJob is not null)
        {
            OneOf<AppError, None>? result = null;
            bool failed = false;

            try
            {
                // If the job was suspended before, we set it to "New" now, because it shouldn't be suspend anymore.
                // In the future, we might invent a JobStatus.Resumed state or something, if we want to keep track of if a job was continued.
                CurrentJob.Status = JobStatus.New;
                result = await CurrentJob.StartJobAsync();

                switch (result.Value.Value)
                {
                    case AppError appError:
                        Logger.LogError(
                            $"{GetType().Name}: [{Schema.Name}] job failed - job type {CurrentJob.GetType()}"
                        );
                        Logger.LogError(appError.Message);
                        failed = true;

                        break;
                    case None:
                        break;
                }
            }
            catch (Exception e)
            {
                Logger.LogError(
                    $"{GetType().Name}: [{Schema.Name}] job failed - job type {CurrentJob.GetType()} - threw exception: {e.Message}"
                );
                failed = true;
            }

            if (failed)
            {
                Jobs = Jobs.Where(job =>
                        job.ParentJob?.Id != CurrentJob.Id && job.Id != CurrentJob.Id
                    )
                    .ToList();
            }

            if (result is not null)
            {
                Logger.LogInformation($"{GetType().Name}: [{Schema.Name}] run job completed");
            }

            CurrentJob = null;

            Busy = false;
            return result ?? new None();
        }

        return new None();
    }

    public PlayerCharacter(CharacterSchema characterSchema)
    {
        Schema = characterSchema;
        GameState = GameServiceProvider.GetInstance().GetService<GameState>()!;
        ApiRequester = GameServiceProvider.GetInstance().GetService<ApiRequester>()!;
        Logger = AppLogger.loggerFactory.CreateLogger<PlayerCharacter>();

        PlayerActionService = new PlayerActionService(
            AppLogger.loggerFactory.CreateLogger<PlayerActionService>(),
            GameState,
            this
        );
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
        if (Schema.X == x && Schema.Y == y)
        {
            return;
        }
        await PreTaskHandler();

        var _body = JsonSerializer.Serialize(new { x = x, y = y });
        StringContent body = new StringContent(_body, Encoding.UTF8, MediaType);

        var response = await ApiRequester.PostAsync($"/my/{Schema.Name}/action/move", body);

        if (((int)response.StatusCode) == 490)
        {
            return;
        }

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

        var response = await ApiRequester.PostAsync($"/my/{Schema.Name}/action/fight", null);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<FightResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;

        foreach (var character in result.Data.Characters)
        {
            var matchingGameStateCharacter = GameState.Characters.Find(_character =>
                _character.Schema.Name == character.Name
            );

            matchingGameStateCharacter!.PostTaskHandler(result.Data.Cooldown, character);
        }

        return result;
    }

    public async Task Rest()
    {
        if (Schema.Hp == Schema.MaxHp)
        {
            return;
        }
        await PreTaskHandler();

        var response = await ApiRequester.PostAsync($"/my/{Schema.Name}/action/rest", null);

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

        var response = await ApiRequester.PostAsync($"/my/{Schema.Name}/action/gathering", null);

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
        var response = await ApiRequester.PostAsync($"/my/{Schema.Name}/action/crafting", body);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterResponse>(
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
        var response = await ApiRequester.PostAsync(
            $"/my/{Schema.Name}/action/bank/deposit/gold",
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
        var response = await ApiRequester.PostAsync(
            $"/my/{Schema.Name}/action/bank/withdraw/gold",
            body
        );

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BankGoldTransactionResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    public async Task DepositBankItem(List<WithdrawOrDepositItemRequest> depositItems)
    {
        await PreTaskHandler();

        string _body = JsonSerializer.Serialize(depositItems);
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");
        var response = await ApiRequester.PostAsync(
            $"/my/{Schema.Name}/action/bank/deposit/item",
            body
        );

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    public async Task<OneOf<AppError, None>> WithdrawBankItem(
        List<WithdrawOrDepositItemRequest> withdrawItems
    )
    {
        await PreTaskHandler();

        string _body = JsonSerializer.Serialize(withdrawItems);
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");
        var response = await ApiRequester.PostAsync(
            $"/my/{Schema.Name}/action/bank/withdraw/item",
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
        var response = await ApiRequester.PostAsync($"/my/{Schema.Name}/action/use", body);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    /**
     * For a "smarter" way to equip items, e.g. don't have to tell in which slot.
    */
    public async Task SmartItemEquip(string itemCode, int quantity = 1)
    {
        await PlayerActionService.SmartItemEquip(itemCode, quantity);
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
        var response = await ApiRequester.PostAsync($"/my/{Schema.Name}/action/equip", body);

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
        var response = await ApiRequester.PostAsync($"/my/{Schema.Name}/action/equip", body);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    public async Task<RecycleResponse> Recycle(string itemCode, int quantity)
    {
        await PreTaskHandler();

        string _body = JsonSerializer.Serialize(new { code = itemCode, quantity });
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");
        var response = await ApiRequester.PostAsync($"/my/{Schema.Name}/action/recycling", body);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<RecycleResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);

        return result;
    }

    public async Task TaskNew()
    {
        await PreTaskHandler();

        var response = await ApiRequester.PostAsync($"/my/{Schema.Name}/action/task/new", null);

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
        var response = await ApiRequester.PostAsync($"/my/{Schema.Name}/action/task/trade", body);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    public async Task<TasksExchangeResponse> TaskExchange()
    {
        await PreTaskHandler();

        var response = await ApiRequester.PostAsync(
            $"/my/{Schema.Name}/action/task/exchange",
            null
        );

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TasksExchangeResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);

        return result;
    }

    public async Task TaskComplete()
    {
        await PreTaskHandler();

        var response = await ApiRequester.PostAsync(
            $"/my/{Schema.Name}/action/task/complete",
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

        var response = await ApiRequester.PostAsync($"/my/{Schema.Name}/action/task/cancel", null);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GenericCharacterResponse>(
            content,
            ApiRequester.getJsonOptions()
        )!;
        PostTaskHandler(result.Data.Cooldown, result.Data.Character);
    }

    public async Task NpcBuyItem(string itemCode, int quantity)
    {
        await PreTaskHandler();

        string _body = JsonSerializer.Serialize(new { code = itemCode, quantity });
        StringContent body = new StringContent(_body, Encoding.UTF8, "application/json");

        var response = await ApiRequester.PostAsync($"/my/{Schema.Name}/action/npc/buy", body);

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
        var response = await ApiRequester.PostAsync($"/my/{Schema.Name}/action/delete", body);

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
            Schema = character;
        }
    }

    public async Task<OneOf<AppError, None>> NavigateTo(string code, ContentType contentType)
    {
        return await PlayerActionService.NavigateTo(code, contentType);
    }

    public InventorySlot? GetItemFromInventory(string code)
    {
        return Schema.Inventory.FirstOrDefault(item => item.Code == code);
    }

    public List<ItemInInventory> GetItemsFromInventoryWithType(string type)
    {
        List<ItemInInventory> items = [];

        foreach (var item in Schema.Inventory)
        {
            var matchingItem = GameState.Items.FirstOrDefault(_item => _item.Code == item.Code);

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

        foreach (var item in Schema.Inventory)
        {
            var matchingItem = GameState.Items.FirstOrDefault(_item => _item.Code == item.Code);

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
        foreach (var item in Schema.Inventory)
        {
            inventorySpaceUsed += item.Quantity;
        }

        return inventorySpaceUsed;
    }

    public int GetInventorySpaceLeft()
    {
        int inventorySpaceUsed = GetInventorySpaceUsed();

        return Schema.InventoryMaxItems - inventorySpaceUsed;
    }

    public OneOf<EquipmentSlot, AppError> GetEquipmentSlot(string slot)
    {
        var prop = Schema.GetType().GetProperty(slot);
        if (prop is null || prop.PropertyType != typeof(string))
        {
            return new AppError($"Invalid slot {slot}");
        }

        string? value = (string?)prop.GetValue(Schema);

        if (value is null)
        {
            return new AppError($"Invalid value in slot {slot} - {value}");
        }
        else if (value == "")
        {
            return new EquipmentSlot
            {
                Slot = slot,
                Code = "",
                Quantity = 1,
            };
        }

        var itemSlot = new EquipmentSlot
        {
            Slot = slot,
            Code = value,
            Quantity = 1,
        };

        if (slot == PlayerItemSlot.Utility1Slot)
        {
            itemSlot.Quantity = Schema.Utility1SlotQuantity;
        }
        else if (slot == PlayerItemSlot.Utility2Slot)
        {
            itemSlot.Quantity = Schema.Utility2SlotQuantity;
        }

        return itemSlot;
    }
}
