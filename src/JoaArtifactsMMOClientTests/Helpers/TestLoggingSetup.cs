using System.Runtime.CompilerServices;
using Application;

namespace JoaArtifactsMMOClientTests.Helpers;

internal static class TestLoggingSetup
{
    [ModuleInitializer]
    public static void Initialize()
    {
        AppLogger.DisableLogging = true;
    }
}
