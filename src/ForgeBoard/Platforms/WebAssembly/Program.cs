using Uno.UI.Hosting;

namespace ForgeBoard;

public class Program
{
    public static async Task Main(string[] args)
    {
        App.InitializeLogging();

        UnoPlatformHost host = UnoPlatformHostBuilder
            .Create()
            .App(() => new App())
            .UseWebAssembly()
            .Build();

        await host.RunAsync();
    }
}
