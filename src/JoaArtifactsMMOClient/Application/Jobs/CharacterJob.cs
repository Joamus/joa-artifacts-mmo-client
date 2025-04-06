using OneOf;

namespace Application.Jobs;

public interface ICharacterJob
{
    public abstract Task<OneOf<JobError>> RunAsync();

    // Implement later maybe
    public abstract Task<OneOf<JobError>> Interrupt();
}
