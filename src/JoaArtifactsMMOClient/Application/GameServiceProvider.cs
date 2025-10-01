namespace Application.Services;

public static class GameServiceProvider
{
    private static IServiceProvider _serviceProvider { get; set; }

    public static IServiceProvider GetInstance()
    {
        if (_serviceProvider is null)
        {
            throw new Exception("Has not yet been assigned");
        }
        return _serviceProvider;
    }

    public static void SetInstance(IServiceProvider serviceProvider)
    {
        if (_serviceProvider is not null)
        {
            throw new Exception("Can only assign once");
        }

        _serviceProvider = serviceProvider;
    }
}
