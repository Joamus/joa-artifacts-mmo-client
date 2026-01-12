namespace Application.Services.Combat;

public class DeterministicCritCalculator : ICritCalculator
{
    public int CritChance { get; set; }

    public int AccCritChance { get; set; }
    public int AddedCritChance { get; set; }

    private bool isFirstRound { get; set; } = true;

    public DeterministicCritCalculator(int critChance, int addedCritChance = 0)
    {
        CritChance = critChance;
        AccCritChance = critChance;
        AddedCritChance = addedCritChance;
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

        if (AccCritChance >= 100)
        {
            AccCritChance -= 100;
            wasCrit = true;
        }

        AccCritChance += CritChance;

        return wasCrit;
    }
}
