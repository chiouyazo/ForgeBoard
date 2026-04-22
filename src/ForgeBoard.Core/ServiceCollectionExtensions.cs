using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Core.Data;
using ForgeBoard.Core.Services;
using ForgeBoard.Core.Services.Build;
using ForgeBoard.Core.Services.PostProcessors;
using ForgeBoard.Core.Services.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeBoard.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddForgeBoardCore(
        this IServiceCollection services,
        string? dataDirectory = null,
        string? tempDirectory = null
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ForgeBoardDatabase>();

        services.AddSingleton<IAppPaths>(provider =>
        {
            AppPaths paths = new AppPaths(dataDirectory, tempDirectory);
            paths.EnsureDirectoriesExist();
            return paths;
        });

        services.AddSingleton<PackerService>();
        services.AddSingleton<IPackerService>(provider =>
            provider.GetRequiredService<PackerService>()
        );

        services.AddSingleton<IPostProcessor, ConvertVhdPostProcessor>();
        services.AddSingleton<IPostProcessor, CompressBoxPostProcessor>();
        services.AddSingleton<IPostProcessor, ChecksumPostProcessor>();

        services.AddSingleton<ArtifactPublisher>();
        services.AddSingleton<VmLauncher>();
        services.AddSingleton<BuildFileServer>();
        services.AddSingleton<BuildReadinessChecker>();
        services.AddSingleton<DirectBuildEngine>();
        services.AddSingleton<PackerBuildEngine>();
        services.AddSingleton<BuildPhaseExecutor>();
        services.AddSingleton<IBuildOrchestrator, BuildOrchestrator>();
        services.AddSingleton<IPackerTemplateGenerator, PackerTemplateGenerator>();
        services.AddSingleton<IFeedManager, FeedManager>();
        services.AddSingleton<IImageManager, ImageManager>();
        services.AddSingleton<ICacheService, CacheService>();

        services.AddSingleton<IFeedAdapter, LocalFeedAdapter>();
        services.AddSingleton<IFeedAdapter, SmbFeedAdapter>();

        services
            .AddHttpClient<UrlFeedAdapter>()
            .ConfigurePrimaryHttpMessageHandler(() =>
                new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                }
            );
        services.AddSingleton<IFeedAdapter>(provider =>
            provider.GetRequiredService<UrlFeedAdapter>()
        );

        services
            .AddHttpClient<NexusFeedAdapter>(client =>
            {
                client.Timeout = TimeSpan.FromHours(4);
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
                new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                }
            );
        services.AddSingleton<IFeedAdapter>(provider =>
            provider.GetRequiredService<NexusFeedAdapter>()
        );

        return services;
    }
}
