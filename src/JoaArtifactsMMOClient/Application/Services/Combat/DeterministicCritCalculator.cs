namespace Application.Services.Combat;

public class DeterministicCritCalculator : ICritCalculator
{
    private Random Random { get; set; }
    public int CritChance { get; set; }

    public int AccCritChance { get; set; }
    public int AddedCritChance { get; set; }

    private bool isFirstRound { get; set; } = true;

    public DeterministicCritCalculator(int critChance, int addedCritChance = 0)
    {
        CritChance = critChance;
        AccCritChance = critChance;
        AddedCritChance = addedCritChance;
        // Always use same seed, to make the RNG "deterministic"
        Random = new Random(1);
    }

    public void Reset()
    {
        isFirstRound = true;
        AccCritChance = CritChance;
    }

    public bool CalculateIsCriticalStrike()
    {
        if (isFirstRound)
        {
            AccCritChance += AddedCritChance;
            isFirstRound = false;
        }

        bool wasCrit = false;

        double randomRoll = Random.NextDouble() * 100;
        double critRoll = randomRoll + AccCritChance;

        // if (AccCritChance >= 100)
        if (critRoll >= 100)
        {
            // AccCritChance -= 100;
            // AccCritChance -= (int)critRoll;
            // Just reset crit chance for now
            AccCritChance = 0;
            wasCrit = true;
        }

        AccCritChance += CritChance;

        return wasCrit;
    }
}
