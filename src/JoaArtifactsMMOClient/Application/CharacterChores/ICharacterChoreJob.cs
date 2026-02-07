using Application.Jobs;

namespace Applicaton.Jobs.Chores;

public interface ICharacterChoreJob
{
    public Task<bool> NeedsToBeDone();
}
