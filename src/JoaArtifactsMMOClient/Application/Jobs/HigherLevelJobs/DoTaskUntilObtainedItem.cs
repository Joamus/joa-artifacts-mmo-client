using Application.ArtifactsApi.Schemas;
using Application.Character;
using Application.Dtos;
using Application.Errors;
using Applicaton.Services.FightSimulator;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class DoTaskUntilObtainedItem : CharacterJob
{
    public TaskType Type { get; private set; }

    public int Amount { get; private set; }

    public DoTaskUntilObtainedItem(
        PlayerCharacter playerCharacter,
        GameState gameState,
        TaskType type,
        string itemCode,
        int amount
    )
        : base(playerCharacter, gameState)
    {
        Type = type;
        Code = itemCode;
        Amount = amount;
    }

    public override async Task<OneOf<AppError, None>> RunAsync()
    {
        _logger.LogInformation(
            $"{GetType().Name} run started - for {_playerCharacter.Character.Name}"
        );

        // This job essentially just keeps queueing Monster/Item task jobs before it self, and then checking to see if the character has obtained the goal or not.

        int amountInInventory = _playerCharacter.GetItemFromInventory(Code)?.Quantity ?? 0;

        if (amountInInventory < Amount)
        {
            CharacterJob task =
                Code == TaskType.monsters.ToString()
                || _playerCharacter.Character.TaskType == TaskType.monsters.ToString()
                    ? new MonsterTask(_playerCharacter, _gameState)
                    : new ItemTask(_playerCharacter, _gameState);

            _playerCharacter.QueueJobsBefore(Id, [task]);
            return new None();
        }

        _logger.LogInformation(
            $"{GetType().Name} completed for for {_playerCharacter.Character.Name} - progress {Code} ({amountInInventory}/{Amount})"
        );

        return new None();
    }
}
