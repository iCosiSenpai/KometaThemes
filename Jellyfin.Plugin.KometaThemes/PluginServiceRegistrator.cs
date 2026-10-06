using System;
using System.Net.Http.Headers;
using Jellyfin.Plugin.KometaThemes.AniList;
using Jellyfin.Plugin.KometaThemes.AnimeThemes;
using Jellyfin.Plugin.KometaThemes.Api;
using Jellyfin.Plugin.KometaThemes.Caching;
using Jellyfin.Plugin.KometaThemes.Http;
using Jellyfin.Plugin.KometaThemes.Library;
using Jellyfin.Plugin.KometaThemes.Resolving;
using Jellyfin.Plugin.KometaThemes.Sync;
using Jellyfin.Plugin.KometaThemes.Themes;
using Jellyfin.Plugin.KometaThemes.Web;
using Jellyfin.Plugin.KometaThemes.YouTube;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes;

/// <summary>
/// Registers the plugin's services.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    private const string AnimeThemesBudget = "AnimeThemes";
    private const string AniListBudget = "AniList";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection.AddSingleton<IResolutionCache, JsonResolutionCache>();
        serviceCollection.AddSingleton<AnimeThemesClient>();
        serviceCollection.AddSingleton<AniListClient>();
        serviceCollection.AddSingleton<TitleSearchResolver>();
        serviceCollection.AddSingleton<AnimeResolver>();
        serviceCollection.AddSingleton<SeasonResolver>();

        serviceCollection.AddSingleton<FolderStateStore>();
        serviceCollection.AddSingleton<FolderLocks>();
        serviceCollection.AddSingleton<TranscodeGate>();
        serviceCollection.AddSingleton<ThemeInstaller>();

        serviceCollection.AddSingleton<LibraryScope>();
        serviceCollection.AddSingleton<ItemTargetFinder>();
        serviceCollection.AddSingleton<ItemRefresher>();
        serviceCollection.AddSingleton<LibraryIndex>();

        serviceCollection.AddSingleton<FailedItemsStore>();
        serviceCollection.AddSingleton<ActivityLog>();
        serviceCollection.AddSingleton<ItemProcessor>();
        serviceCollection.AddSingleton<SyncRunner>();
        serviceCollection.AddSingleton<ItemDetailsBuilder>();

        serviceCollection.AddSingleton<ManagedYouTubeExtractor>();
        serviceCollection.AddSingleton<YouTubeImportService>();

        serviceCollection.AddSingleton<LibraryWatcher>();
        serviceCollection.AddHostedService(provider => provider.GetRequiredService<LibraryWatcher>());
        serviceCollection.AddHostedService<IndexInjection>();

        // Budgets are singletons: handler chains are rebuilt every few minutes and must not reset them.
        serviceCollection.AddKeyedSingleton(AnimeThemesBudget, (_, _) => new ApiThrottle(AnimeThemesBudget, () => Plugin.Instance?.Configuration?.RateLimitPerMinute ?? 60));
        serviceCollection.AddKeyedSingleton(AniListBudget, (_, _) => new ApiThrottle(AniListBudget, () => 25));

        var userAgent = new ProductInfoHeaderValue("KometaThemes", typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "2");
        var contact = new ProductInfoHeaderValue("(+https://github.com/iCosiSenpai/KometaThemes)");

        serviceCollection.AddHttpClient(AnimeThemesClient.HttpClientName, client =>
            {
                client.BaseAddress = new Uri("https://api.animethemes.moe/");
                client.DefaultRequestHeaders.UserAgent.Add(userAgent);
                client.DefaultRequestHeaders.UserAgent.Add(contact);
                client.Timeout = TimeSpan.FromMinutes(5);
            })
            .AddHttpMessageHandler(provider => Retry(provider, TimeSpan.FromSeconds(45)))
            .AddHttpMessageHandler(provider => new ThrottleHandler(provider.GetRequiredKeyedService<ApiThrottle>(AnimeThemesBudget)));

        serviceCollection.AddHttpClient(AniListClient.HttpClientName, client =>
            {
                client.BaseAddress = new Uri("https://graphql.anilist.co/");
                client.DefaultRequestHeaders.UserAgent.Add(userAgent);
                client.DefaultRequestHeaders.UserAgent.Add(contact);
                client.Timeout = TimeSpan.FromMinutes(5);
            })
            .AddHttpMessageHandler(provider => Retry(provider, TimeSpan.FromSeconds(30)))
            .AddHttpMessageHandler(provider => new ThrottleHandler(provider.GetRequiredKeyedService<ApiThrottle>(AniListBudget)));

        // Theme files: the client timeout would also cover the streamed body, which the installer
        // bounds itself, so it is off here; the retry handler limits the wait for the headers.
        serviceCollection.AddHttpClient(ThemeInstaller.HttpClientName, client =>
            {
                client.DefaultRequestHeaders.UserAgent.Add(userAgent);
                client.DefaultRequestHeaders.UserAgent.Add(contact);
                client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            })
            .AddHttpMessageHandler(provider => Retry(provider, TimeSpan.FromSeconds(60)));

        // The YouTube extractor retries its own chunked requests; a second retry layer would multiply them.
        serviceCollection.AddHttpClient("YouTube", client =>
        {
            client.DefaultRequestHeaders.UserAgent.Add(userAgent);
            client.Timeout = TimeSpan.FromMinutes(10);
        });
    }

    private static RetryHandler Retry(IServiceProvider provider, TimeSpan attemptTimeout)
        => new(provider.GetRequiredService<ILogger<RetryHandler>>()) { AttemptTimeout = attemptTimeout };
}
