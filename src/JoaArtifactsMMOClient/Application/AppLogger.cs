namespace Application;

public static class AppLogger
{
    // public T ILogger<T> CreateLogger()

    public readonly static Action<ILoggingBuilder> options = builder =>
    {
        builder.AddConsole();
    };
}
