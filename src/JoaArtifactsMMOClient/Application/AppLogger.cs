namespace Application;

public static class AppLogger
{
    // public T ILogger<T> CreateLogger()
    public static readonly Action<ILoggingBuilder> options = builder =>
    {
        builder.AddConsole();
    };
    public static ILoggerFactory loggerFactory { get; } = LoggerFactory.Create(options);
}
