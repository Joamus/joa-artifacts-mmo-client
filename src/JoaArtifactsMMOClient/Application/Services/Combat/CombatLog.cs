using Application.ArtifactsApi.Schemas;

namespace Application.Services.Combat;

public class CombatLog
{
    public List<string> combatLog { get; private set; } = [];

    public CombatLog(FightEntity attacker, FightEntity defender)
    {
        combatLog = [];
        combatLog.Add(
            $"Fight start: attacker {attacker.Name} HP: {attacker.Hp}/{attacker.MaxHp} vs. {defender.Name} HP: {defender.Hp}/{defender.MaxHp}"
        );
    }

    public void Log(int turnNumber, FightEntity attacker, FightEntity defender, string message)
    {
        combatLog.Add(
            $"Turn number {turnNumber}: {message}. {attacker.Name} HP: {attacker.Hp}/{attacker.MaxHp}. {defender.Name} HP: {defender.Hp}/{defender.MaxHp}"
        );
    }
}
