namespace Applicaton.Jobs.Chores;

public interface ICharacterChoreJob
{
    public Task<bool> NeedsToBeDone();
}

public enum ChorePriority
{
    Low,
    High,
}
