using Application;
using Application.Services.ApiServices;
using Infrastructure;
using NSubstitute;

namespace JoaArtifactsMMOClientTests.Helpers;

public static class ServiceHelper
{
    public static ApiRequester GetTestApiRequester()
    {
        return Substitute.For<ApiRequester>("dummy_token");
    }

    public static GameState GetEmptyGameState()
    {
        var apiRequester = GetTestApiRequester();

        GameState gameState = new GameState(
            Substitute.For<AccountRequester>(apiRequester, "dummy_account_name"),
            apiRequester
        );

        return gameState;
    }
}
