using Application;
using Microsoft.Extensions.DependencyInjection;

// ServiceCollection collection = new ServiceCollection();
//

GameLoader loader = new GameLoader();

var _ = await loader.Start();
