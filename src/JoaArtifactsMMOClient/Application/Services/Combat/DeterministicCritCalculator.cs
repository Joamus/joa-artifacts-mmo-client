namespace Application.Services.Combat;

public class DeterministicCritCalculator : ICritCalculator
{
    private Random Random { get; set; }
    public int CritChance { get; set; }

    public int AccCritChance { get; set; }
    public bool KeepAccCritChance { get; init; }
    public int AddedCritChance { get; set; }

    private bool IsFirstRound { get; set; } = true;

    // public DeterministicCritCalculator(int critChance, int addedCritChance = 0)
    public DeterministicCritCalculator(DeterministicCritCalculatorParams critCalculatorParams)
    {
        CritChance = critCalculatorParams.CritChance;
        AccCritChance = CritChance;
        AddedCritChance = critCalculatorParams.AddedCritChance;
        // Use different seeds for different participant types - still "deterministic", but a bit different
        Random = new Random((int)critCalculatorParams.CritType);
        KeepAccCritChance =
            // Boss type not used atm
            critCalculatorParams.CritType == CritType.Boss
            || critCalculatorParams.CritType == CritType.Monster;
    }

    public void Reset()
    {
        IsFirstRound = true;
        AccCritChance = CritChance;
    }

    public bool CalculateIsCriticalStrike()
    {
        if (IsFirstRound)
        {
            AccCritChance += AddedCritChance;
            IsFirstRound = false;
        }

        bool wasCrit = false;

        double randomRoll = Random.NextDouble() * 100;
        double critRoll = randomRoll + AccCritChance;

        // if (AccCritChance >= 100)
        if (critRoll >= 100)
        {
            // We keep the accumulated chance for next roll
            AccCritChance -= 100;
            // AccCritChance -= (int)critRoll;
            // AccCritChance = 0;
            wasCrit = true;
        }

        AccCritChance += CritChance;

        return wasCrit;
    }
}

public record DeterministicCritCalculatorParams
{
    public required int CritChance { get; init; }
    public int AddedCritChance { get; init; } = 0;
    public required CritType CritType { get; init; }
}

public enum CritType
{
    Player = 0,
    Monster = 1,

    Boss = 2,
    Other = 3,
}
