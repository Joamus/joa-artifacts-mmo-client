using Application.Artifacts.Schemas;
using Application.ArtifactsApi.Schemas;
using Application.ArtifactsApi.Schemas.Responses;
using Application.Character;
using Application.Errors;
using Application.Jobs;
using Application.Jobs.Orchestrators;
using Application.Records;
using Applicaton.Services.FightSimulator;

namespace Application.Services;

public static class MonsterService
{
    public static async Task<CharacterJob?> GetRaidBossJobIfPossible(
        PlayerCharacter character,
        MonsterSchema monster,
        GameState gameState
    )
    {
        var bestCandidates = FightBossOrchestrator.GetBestCandidatesToFight(character, gameState);

        bool canFight = (
            await FightBossOrchestrator.CanFulfillRequirementsForFightingBoss(
                character,
                bestCandidates,
                gameState,
                monster
            )
        ).ShouldFight;

        if (canFight)
        {
            var result = new InitializeFightBoss(
                new InitializeFightBossJobParams
                {
                    Character = character,
                    GameState = gameState,
                    OtherCharacters = bestCandidates,
                    Monster = monster,
                    AllowUsingMaterialsFromInventory = true,
                    Amount = 1000,
                    ItemCode = null,
                }
            );

            return result;
        }

        return null;
    }
}
