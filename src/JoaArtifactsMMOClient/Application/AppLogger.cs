namespace Application;

public static class AppLogger
{
    // public T ILogger<T> CreateLogger()

    private static ILogger? _logger;

    public static ILogger GetLogger()
    {
        if (_logger is null)
        {
            _logger = LoggerFactory.Create(options).CreateLogger("AppLogger");
        }

        return _logger;
    }

    public static readonly Action<ILoggingBuilder> options = builder =>
    {
        builder.AddConsole();
    };
    public static ILoggerFactory loggerFactory { get; } = LoggerFactory.Create(options);
}
