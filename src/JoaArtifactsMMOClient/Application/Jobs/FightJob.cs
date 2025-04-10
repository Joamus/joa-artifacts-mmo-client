using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Applicaton.Services.FightSimulator;
using OneOf;
using OneOf.Types;

namespace Application.Jobs;

public class FightJob : CharacterJob
{
    public FightJob(PlayerCharacter playerCharacter, string code, int amount, GameState gameState)
        : base(playerCharacter, code, amount, gameState) { }

    public override async Task<OneOf<JobError, None>> RunAsync()
    {
        _logger.LogInformation(
            $"FightJob started for {_playerCharacter._character.Name} - fighting ${_code} (${_progressAmount}/${_amount})"
        );
        await _playerCharacter.NavigateTo(_code, ContentType.Monster);

        MonsterSchema? matchingMonster = _gameState._monsters.Find(monster =>
            monster.Code == _code
        );

        if (matchingMonster is null)
        {
            return new JobError($"Monster with code {_code} could not be found");
        }

        if (_playerCharacter._character.Hp != _playerCharacter._character.MaxHp)
        {
            await _playerCharacter.Rest();
        }

        var fightSimulation = FightSimulatorService.CalculateFightOutcome(
            _playerCharacter._character,
            matchingMonster
        );

        if (
            fightSimulation.Result == FightResult.Win
            && fightSimulation.PlayerHp >= (_playerCharacter._character.MaxHp * 0.4)
        )
        {
            var result = await _playerCharacter.Fight();

            if (result.Value is JobError)
            {
                return (JobError)result.Value;
            }
            else if (result.Value is FightResponse)
            {
                _amount++;
                if (_amount >= _progressAmount)
                {
                    _logger.LogInformation(
                        $"FightJob completed for {_playerCharacter._character.Name} - fought ${_code} (${_progressAmount}/${_amount})"
                    );
                    return new None();
                }
                else
                {
                    return await RunAsync();
                }
            }
        }

        return new None();
    }
}
