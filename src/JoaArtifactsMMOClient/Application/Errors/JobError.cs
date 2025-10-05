namespace Application.Errors;

public class AppError : SystemException
{
    // public string Message { get; private set; }

    public ErrorStatus Status { get; init; }

    public static readonly ILogger<AppError> _logger = LoggerFactory
        .Create(AppLogger.options)
        .CreateLogger<AppError>();

    public AppError(string message)
        : base(message)
    {
        Status = ErrorStatus.Undefined;
        _logger.LogDebug($"JobError: {message}");
    }

    public AppError(string message, ErrorStatus status)
        : base(message)
    {
        Status = status;
        _logger.LogDebug($"JobError: {message}");
    }
}

public enum ErrorStatus
{
    Undefined,
    NotFound,
    InsufficientSkill,
}
