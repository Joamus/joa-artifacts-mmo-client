using Application.Jobs;
using OneOf;
using OneOf.Types;

namespace Application.Character;

public class CharacterJobHandler
{
    private DateTime cooldownUntil;

    private List<ICharacterJob> _jobs;

    private ICharacterJob? _currentJob;

    private PlayerCharacter _character;

    public CharacterJobHandler(PlayerCharacter character)
    {
        _character = character;
        cooldownUntil = DateTime.UtcNow;
    }

    // public Task HandleJob(ICharacterCommand) {
    // }
    //

    public void QueueJob(ICharacterJob job, bool highestPriority = true)
    {
        if (highestPriority)
        {
            _jobs.Insert(0, job);
        }
        else
        {
            _jobs.Add(job);
        }
    }

    public void ClearJobs()
    {
        _jobs = [];
    }

    public async Task<OneOf<None, JobError>> ProcessNextJob()
    {
        if (_jobs.Count > 0)
        {
            _currentJob = _jobs[0];
            _jobs.RemoveAt(0);

            var result = await _currentJob.RunAsync();
            return result.Match(jobError => jobError);
        }

        return new None();
    }
}
