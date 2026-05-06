using Application.Jobs;

namespace Applicaton.Jobs.Chores;

public interface ICharacterChoreJob
{
    public Task<bool> NeedsToBeDone(ChorePriority priority);
}

public enum ChorePriority
{
    Low,
    High,
}
