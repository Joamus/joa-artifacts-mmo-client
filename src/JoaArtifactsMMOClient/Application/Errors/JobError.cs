namespace Application.Errors;

public class JobError
{
    public string Message { get; private set; }

    public JobStatus Status { get; init; }

    public static readonly ILogger<JobError> _logger = LoggerFactory
        .Create(AppLogger.options)
        .CreateLogger<JobError>();

    public JobError(string message)
    {
        Message = message;
        Status = JobStatus.Undefined;
        _logger.LogDebug($"JobError: {message}");
    }

    public JobError(string message, JobStatus status)
    {
        Message = message;
        Status = status;
        _logger.LogDebug($"JobError: {message}");
    }
}

public enum JobStatus
{
    Undefined,
    NotFound,
    InsufficientSkill,
}
