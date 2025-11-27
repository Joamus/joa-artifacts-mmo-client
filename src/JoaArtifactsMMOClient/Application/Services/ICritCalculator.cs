namespace Application.Services;

public interface ICritCalculator
{
    public bool CalculateIsCriticalStrike();

    public void Reset();
}
