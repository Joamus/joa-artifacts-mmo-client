using Application;
using Application.Services.ApiServices;
using Infrastructure;
using Moq;

namespace JoaArtifactsMMOClientTests.Helpers;

public static class ServiceHelper
{
    public static ApiRequester GetTestApiRequester()
    {
        return new Mock<ApiRequester>("dummy_token").Object;
    }

    public static GameState GetEmptyGameState()
    {
        var apiRequester = GetTestApiRequester();

        GameState gameState = new GameState(
            new Mock<AccountRequester>(apiRequester, "dummy_account_name").Object,
            apiRequester
        );

        return gameState;
    }
}
