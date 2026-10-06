using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.AnimeThemes;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Library;
using Jellyfin.Plugin.KometaThemes.Models;
using Jellyfin.Plugin.KometaThemes.Resolving;
using Jellyfin.Plugin.KometaThemes.Sync;
using Jellyfin.Plugin.KometaThemes.Themes;
using Jellyfin.Plugin.KometaThemes.YouTube;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// The library list, the anime page and everything it can do.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("KometaThemes")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class KometaItemsController : KometaControllerBase
{
    private const string NotManaged = "This anime is not in a library KometaThemes manages. Add its library in Settings.";

    private readonly ILibraryManager _libraryManager;
    private readonly LibraryScope _scope;
    private readonly LibraryIndex _index;
    private readonly ItemTargetFinder _targets;
    private readonly ItemDetailsBuilder _details;
    private readonly ItemProcessor _processor;
    private readonly AnimeResolver _resolver;
    private readonly AnimeThemesClient _client;
    private readonly ThemeInstaller _installer;
    private readonly ItemRefresher _refresher;
    private readonly FailedItemsStore _problems;
    private readonly ActivityLog _activity;
    private readonly YouTubeImportService _youTube;
    private readonly LibraryWatcher _events;
    private readonly ILogger<KometaItemsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KometaItemsController"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="scope">Library scope.</param>
    /// <param name="index">Library index.</param>
    /// <param name="targets">Target finder.</param>
    /// <param name="details">Detail builder.</param>
    /// <param name="processor">Item processor.</param>
    /// <param name="resolver">Anime resolver.</param>
    /// <param name="client">animethemes.moe client.</param>
    /// <param name="installer">Theme installer.</param>
    /// <param name="refresher">Item refresher.</param>
    /// <param name="problems">Problem store.</param>
    /// <param name="activity">Activity list.</param>
    /// <param name="youTube">YouTube import.</param>
    /// <param name="events">Library events, to avoid processing an item twice.</param>
    /// <param name="logger">Logger.</param>
    public KometaItemsController(
        ILibraryManager libraryManager,
        LibraryScope scope,
        LibraryIndex index,
        ItemTargetFinder targets,
        ItemDetailsBuilder details,
        ItemProcessor processor,
        AnimeResolver resolver,
        AnimeThemesClient client,
        ThemeInstaller installer,
        ItemRefresher refresher,
        FailedItemsStore problems,
        ActivityLog activity,
        YouTubeImportService youTube,
        LibraryWatcher events,
        ILogger<KometaItemsController> logger)
    {
        _libraryManager = libraryManager;
        _scope = scope;
        _index = index;
        _targets = targets;
        _details = details;
        _processor = processor;
        _resolver = resolver;
        _client = client;
        _installer = installer;
        _refresher = refresher;
        _problems = problems;
        _activity = activity;
        _youTube = youTube;
        _events = events;
        _logger = logger;
    }

    private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>
    /// Lists the managed series and movies.
    /// </summary>
    /// <returns>Summaries in library order.</returns>
    [HttpGet("Library")]
    public async Task<ActionResult> GetLibrary()
        => Json(await _index.GetAsync(Config, HttpContext.RequestAborted).ConfigureAwait(false));

    /// <summary>
    /// Gets the anime page of a series or movie.
    /// </summary>
    /// <param name="itemId">Series, season, episode or movie ID; seasons and episodes open their series.</param>
    /// <returns>The page data.</returns>
    [HttpGet("Items/{itemId}")]
    public async Task<ActionResult> GetItem([FromRoute] Guid itemId)
    {
        var owner = Owner(itemId, out var error);
        return owner == null ? error! : await DetailAsync(owner).ConfigureAwait(false);
    }

    /// <summary>
    /// Tells the item-page button whether KometaThemes manages an item, without any network lookup.
    /// </summary>
    /// <param name="itemId">Series, season, episode or movie ID.</param>
    /// <returns>Whether it is managed, its series or movie, and the library summary.</returns>
    [HttpGet("Items/{itemId}/Summary")]
    public async Task<ActionResult> GetSummary([FromRoute] Guid itemId)
    {
        var owner = LibraryScope.OwnerOf(_libraryManager.GetItemById(itemId));
        if (owner == null || !_scope.IsInScope(owner, Config))
        {
            return Json(new { managed = false });
        }

        var summary = await _index.SummarizeAsync(owner, Config, HttpContext.RequestAborted).ConfigureAwait(false);
        return Json(new { managed = true, ownerId = owner.Id, summary });
    }

    /// <summary>
    /// Brings one anime up to date now.
    /// </summary>
    /// <param name="itemId">Series or movie ID.</param>
    /// <param name="redownload">Whether to download every wanted file again.</param>
    /// <returns>The page data.</returns>
    [HttpPost("Items/{itemId}/Check")]
    public async Task<ActionResult> Check([FromRoute] Guid itemId, [FromQuery] bool redownload = false)
    {
        var owner = Owner(itemId, out var error);
        if (owner == null)
        {
            return error!;
        }

        _resolver.Forget(owner);
        await RunAsync(owner, redownload).ConfigureAwait(false);
        return await DetailAsync(owner).ConfigureAwait(false);
    }

    /// <summary>
    /// Matches a series, season or movie to an anime entry and downloads its themes.
    /// </summary>
    /// <param name="itemId">Series, season or movie ID.</param>
    /// <param name="request">The chosen entry.</param>
    /// <returns>The page data.</returns>
    [HttpPut("Items/{itemId}/Match")]
    [Consumes(MediaTypeNames.Application.Json)]
    public async Task<ActionResult> SetMatch([FromRoute] Guid itemId, [FromBody] MatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var target = _libraryManager.GetItemById(itemId);
        var owner = Owner(itemId, out var error);
        if (owner == null || target == null)
        {
            return error ?? Error(StatusCodes.Status404NotFound, "This item no longer exists.");
        }

        Anime? anime;
        try
        {
            anime = await _resolver.GetAnimeAsync(request.AnimeId, HttpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return Error(StatusCodes.Status502BadGateway, "animethemes.moe could not be reached. Try again in a moment.");
        }

        if (anime == null)
        {
            return Error(StatusCodes.Status404NotFound, "That entry is not on animethemes.moe.");
        }

        Plugin.MutateConfiguration(c =>
        {
            foreach (var old in c.ManualBindings.Where(b => Guid.TryParse(b.ItemId, out var id) && id == itemId).ToList())
            {
                c.ManualBindings.Remove(old);
            }

            c.ManualBindings.Add(new ManualBindingEntry
            {
                ItemId = itemId.ToString("D", CultureInfo.InvariantCulture),
                AnimeId = anime.Id,
                AnimeName = anime.Name,
                Slug = anime.Slug,
                BoundAt = DateTime.UtcNow,
                Source = "Page",
            });
            c.TrimLists();
        });

        _problems.Remove(owner.Id);
        _activity.Add(ActivityKind.Matched, owner.Id, owner.Name, target.Id == owner.Id ? $"Matched to {anime.Name}" : $"{target.Name} matched to {anime.Name}");
        await RunAsync(owner, false).ConfigureAwait(false);
        return await DetailAsync(owner).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes the owner's match so automatic matching applies again.
    /// </summary>
    /// <param name="itemId">Series, season or movie ID.</param>
    /// <returns>The page data.</returns>
    [HttpDelete("Items/{itemId}/Match")]
    public async Task<ActionResult> ClearMatch([FromRoute] Guid itemId)
    {
        var owner = Owner(itemId, out var error);
        if (owner == null)
        {
            return error!;
        }

        Plugin.MutateConfiguration(c =>
        {
            foreach (var old in c.ManualBindings.Where(b => Guid.TryParse(b.ItemId, out var id) && id == itemId).ToList())
            {
                c.ManualBindings.Remove(old);
            }
        });

        _resolver.Forget(owner);
        await RunAsync(owner, false).ConfigureAwait(false);
        return await DetailAsync(owner).ConfigureAwait(false);
    }

    /// <summary>
    /// Excludes an anime: KometaThemes stops managing it.
    /// </summary>
    /// <param name="itemId">Series or movie ID.</param>
    /// <param name="deleteFiles">Whether to delete the files KometaThemes downloaded for it.</param>
    /// <returns>The page data.</returns>
    [HttpPut("Items/{itemId}/Excluded")]
    public async Task<ActionResult> Exclude([FromRoute] Guid itemId, [FromQuery] bool deleteFiles = false)
    {
        var owner = Owner(itemId, out var error);
        if (owner == null)
        {
            return error!;
        }

        Plugin.MutateConfiguration(c =>
        {
            if (c.FindSkipped(owner.Id) == null)
            {
                c.SkippedItems.Add(new SkippedItemEntry
                {
                    ItemId = owner.Id.ToString("D", CultureInfo.InvariantCulture),
                    Name = owner.Name ?? string.Empty,
                    Type = owner.GetBaseItemKind().ToString(),
                    ProductionYear = owner.ProductionYear,
                    Reason = "Excluded from its page",
                    SkippedUtc = DateTime.UtcNow,
                });
                c.TrimLists();
            }
        });

        _problems.Remove(owner.Id);
        if (deleteFiles)
        {
            var deleted = await DeleteAllAsync(owner).ConfigureAwait(false);
            _activity.Add(ActivityKind.Removed, owner.Id, owner.Name, string.Create(CultureInfo.InvariantCulture, $"Excluded; removed {deleted} file(s)"));
        }

        return await DetailAsync(owner).ConfigureAwait(false);
    }

    /// <summary>
    /// Lets KometaThemes manage an excluded anime again.
    /// </summary>
    /// <param name="itemId">Series or movie ID.</param>
    /// <returns>The page data.</returns>
    [HttpDelete("Items/{itemId}/Excluded")]
    public async Task<ActionResult> Include([FromRoute] Guid itemId)
    {
        var owner = Owner(itemId, out var error);
        if (owner == null)
        {
            return error!;
        }

        Plugin.MutateConfiguration(c =>
        {
            foreach (var entry in c.SkippedItems.Where(s => Guid.TryParse(s.ItemId, out var id) && id == owner.Id).ToList())
            {
                c.SkippedItems.Remove(entry);
            }
        });

        await RunAsync(owner, false).ConfigureAwait(false);
        return await DetailAsync(owner).ConfigureAwait(false);
    }

    /// <summary>
    /// Keeps or drops one theme in one folder, regardless of the settings.
    /// </summary>
    /// <param name="itemId">Series, season or movie ID.</param>
    /// <param name="themeId">Theme ID.</param>
    /// <param name="request">The choice.</param>
    /// <returns>The page data.</returns>
    [HttpPut("Items/{itemId}/Themes/{themeId:int}")]
    [Consumes(MediaTypeNames.Application.Json)]
    public async Task<ActionResult> SetThemeChoice([FromRoute] Guid itemId, [FromRoute] int themeId, [FromBody] ThemeChoiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var owner = Owner(itemId, out var error);
        if (owner == null)
        {
            return error!;
        }

        var folder = FolderOf(owner, itemId);
        if (folder == null)
        {
            return Error(StatusCodes.Status400BadRequest, "Themes cannot be stored for this item.");
        }

        var change = request.Change?.ToLowerInvariant() ?? "both";
        if (change is "audio" or "both")
        {
            await _installer.SetOverrideAsync(folder.Folder, themeId, MediaType.Audio, request.Audio, HttpContext.RequestAborted).ConfigureAwait(false);
        }

        if (change is "video" or "both")
        {
            await _installer.SetOverrideAsync(folder.Folder, themeId, MediaType.Video, request.Video, HttpContext.RequestAborted).ConfigureAwait(false);
        }

        await RunAsync(owner, false).ConfigureAwait(false);
        return await DetailAsync(owner).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes one file KometaThemes wrote. An animethemes.moe theme deleted this way is not downloaded again.
    /// </summary>
    /// <param name="itemId">Series, season or movie ID.</param>
    /// <param name="directory"><c>theme-music</c> or <c>backdrops</c>.</param>
    /// <param name="name">File name.</param>
    /// <returns>The page data.</returns>
    [HttpDelete("Items/{itemId}/Files")]
    public async Task<ActionResult> DeleteFile([FromRoute] Guid itemId, [FromQuery] string directory, [FromQuery] string name)
    {
        var owner = Owner(itemId, out var error);
        if (owner == null)
        {
            return error!;
        }

        var folder = FolderOf(owner, itemId);
        if (folder == null)
        {
            return Error(StatusCodes.Status400BadRequest, "Themes cannot be stored for this item.");
        }

        ThemeRecord? removed;
        try
        {
            removed = await _installer.DeleteRecordedFileAsync(folder.Folder, directory, name, HttpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            return Error(StatusCodes.Status500InternalServerError, "The file could not be deleted: " + ex.Message);
        }

        if (removed == null)
        {
            return Error(StatusCodes.Status404NotFound, "KometaThemes did not write that file, so it does not delete it. Remove it from the folder yourself if you want it gone.");
        }

        _activity.Add(ActivityKind.Removed, owner.Id, owner.Name, "Removed " + name);
        if (_libraryManager.GetItemById(itemId) is { } target)
        {
            await _refresher.RefreshAsync(target, HttpContext.RequestAborted).ConfigureAwait(false);
        }

        return await DetailAsync(owner).ConfigureAwait(false);
    }

    /// <summary>
    /// Searches animethemes.moe for the match dialog.
    /// </summary>
    /// <param name="itemId">The item being matched, to rank results.</param>
    /// <param name="q">Search text.</param>
    /// <returns>Ranked entries.</returns>
    [HttpGet("Items/{itemId}/Search")]
    public async Task<ActionResult> Search([FromRoute] Guid itemId, [FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Error(StatusCodes.Status400BadRequest, "Type a title to search.");
        }

        var item = _libraryManager.GetItemById(itemId);
        try
        {
            var results = await _client.SearchAsync(q.Trim(), HttpContext.RequestAborted).ConfigureAwait(false);
            var ranked = TitleSearchResolver.Rank(item is MediaBrowser.Controller.Entities.TV.Season season ? season.Series : item, q, results);
            return Json(ranked.Take(12).Select(r => ItemDetailsBuilder.ToDto(r.Anime, r.Score)));
        }
        catch (HttpRequestException)
        {
            return Error(StatusCodes.Status502BadGateway, "animethemes.moe could not be reached. Try again in a moment.");
        }
    }

    /// <summary>
    /// Gets an entry with its themes, to preview it before matching.
    /// </summary>
    /// <param name="animeId">animethemes.moe anime ID.</param>
    /// <returns>The entry and its themes.</returns>
    [HttpGet("Anime/{animeId:int}")]
    public async Task<ActionResult> GetAnime([FromRoute] int animeId)
    {
        try
        {
            var anime = await _resolver.GetAnimeAsync(animeId, HttpContext.RequestAborted).ConfigureAwait(false);
            return anime == null
                ? Error(StatusCodes.Status404NotFound, "That entry is not on animethemes.moe.")
                : Json(new { anime = ItemDetailsBuilder.ToDto(anime), themes = ItemDetailsBuilder.Themes(anime) });
        }
        catch (HttpRequestException)
        {
            return Error(StatusCodes.Status502BadGateway, "animethemes.moe could not be reached. Try again in a moment.");
        }
    }

    /// <summary>
    /// Imports a theme from a YouTube link.
    /// </summary>
    /// <param name="itemId">Series, season or movie ID.</param>
    /// <param name="request">Link and details.</param>
    /// <returns>The page data.</returns>
    [HttpPost("Items/{itemId}/YouTube")]
    [Consumes(MediaTypeNames.Application.Json)]
    public async Task<ActionResult> ImportYouTube([FromRoute] Guid itemId, [FromBody] YouTubeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var config = Config;
        if (!config.EnableYouTubeImport)
        {
            return Error(StatusCodes.Status400BadRequest, "YouTube import is off. Turn it on in Settings.");
        }

        if (!YouTubeUrlParser.TryGetVideoId(request.Url, out var videoId))
        {
            return Error(StatusCodes.Status400BadRequest, "That is not a YouTube video link. Paste a youtube.com or youtu.be link.");
        }

        var owner = Owner(itemId, out var error);
        if (owner == null)
        {
            return error!;
        }

        var folder = FolderOf(owner, itemId);
        if (folder == null)
        {
            return Error(StatusCodes.Status400BadRequest, "Themes cannot be stored for this item.");
        }

        var type = string.Equals(request.Type, "ED", StringComparison.OrdinalIgnoreCase) ? ThemeType.ED : ThemeType.OP;
        var sequence = Math.Clamp(request.Sequence, 1, 99);
        var format = request.Format?.ToLowerInvariant() ?? "audio";
        var wantsAudio = format is "audio" or "both";
        var wantsVideo = format is "video" or "both";

        var download = await _youTube.DownloadAsync(videoId, audioOnly: !wantsVideo, HttpContext.RequestAborted).ConfigureAwait(false);
        if (!download.Success)
        {
            return Error(StatusCodes.Status400BadRequest, "YouTube download failed: " + download.Error);
        }

        try
        {
            var title = string.IsNullOrWhiteSpace(request.Title) ? download.Title : request.Title.Trim();
            var code = (type == ThemeType.ED ? "ED" : "OP") + sequence.ToString(CultureInfo.InvariantCulture);
            var baseName = Path.GetFileNameWithoutExtension(ThemeNaming.FileName(code, title, ".x"));
            foreach (var media in new[] { (On: wantsAudio, Media: MediaType.Audio, Volume: config.AudioSettings.VolumePercent), (On: wantsVideo, Media: MediaType.Video, Volume: config.VideoSettings.VolumePercent) })
            {
                if (!media.On)
                {
                    continue;
                }

                var record = new ThemeRecord
                {
                    // Negative and derived from the video, so it never collides with animethemes.moe IDs.
                    ThemeId = -Math.Abs(HashCode.Combine(videoId, media.Media, sequence, type) % int.MaxValue) - 1,
                    Type = type,
                    Sequence = sequence,
                    Slug = videoId,
                    Source = ThemeSource.YouTube,
                    SourceUrl = YouTubeUrlParser.BuildWatchUrl(videoId),
                    Title = title,
                };
                await _installer.ImportAsync(folder, download.FilePath, media.Media, record, baseName, media.Volume, HttpContext.RequestAborted).ConfigureAwait(false);
            }

            _activity.Add(ActivityKind.Imported, owner.Id, owner.Name, $"Imported {code} {title} from YouTube");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            _logger.LogWarning(ex, "YouTube import failed for {Name}", owner.Name);
            return Error(StatusCodes.Status500InternalServerError, "The video could not be converted: " + ex.Message);
        }
        finally
        {
            _youTube.CleanupDownload(download.FilePath);
        }

        if (_libraryManager.GetItemById(itemId) is { } target)
        {
            await _refresher.RefreshAsync(target, HttpContext.RequestAborted).ConfigureAwait(false);
        }

        return await DetailAsync(owner).ConfigureAwait(false);
    }

    private BaseItem? Owner(Guid itemId, out ActionResult? error)
    {
        var owner = LibraryScope.OwnerOf(_libraryManager.GetItemById(itemId));
        if (owner == null)
        {
            error = Error(StatusCodes.Status404NotFound, "This item no longer exists, or it is not a series, season or movie.");
            return null;
        }

        if (!_scope.IsInScope(owner, Config))
        {
            error = Error(StatusCodes.Status400BadRequest, NotManaged);
            return null;
        }

        error = null;
        return owner;
    }

    private ThemeFolder? FolderOf(BaseItem owner, Guid itemId)
    {
        var targets = _targets.For(owner);
        return itemId == owner.Id ? targets.Root : targets.Seasons.FirstOrDefault(s => s.Season.Id == itemId)?.Folder;
    }

    private async Task RunAsync(BaseItem owner, bool redownload)
    {
        _events.MarkProcessed(owner.Id);
        var config = Config;
        if (config.FindSkipped(owner.Id) != null)
        {
            return;
        }

        try
        {
            await _processor.ProcessAsync(owner, config, redownload, HttpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not process {Name}", owner.Name);
            _problems.Record(owner, ProblemKind.DownloadFailed, ex.Message);
        }

        _events.MarkProcessed(owner.Id);
    }

    private async Task<int> DeleteAllAsync(BaseItem owner)
    {
        var targets = _targets.For(owner);
        var deleted = 0;
        var folders = new List<ThemeFolder>();
        if (targets.Root != null)
        {
            folders.Add(targets.Root);
        }

        folders.AddRange(targets.Seasons.Select(s => s.Folder));
        foreach (var folder in folders)
        {
            deleted += await _installer.DeleteAllAsync(folder.Folder, owner is MediaBrowser.Controller.Entities.Movies.Movie ? owner.Id : null, HttpContext.RequestAborted).ConfigureAwait(false);
        }

        if (deleted > 0)
        {
            await _refresher.RefreshAsync(owner, HttpContext.RequestAborted).ConfigureAwait(false);
            foreach (var season in targets.Seasons)
            {
                await _refresher.RefreshAsync(season.Season, HttpContext.RequestAborted).ConfigureAwait(false);
            }
        }

        _index.Invalidate(owner.Id);
        return deleted;
    }

    private async Task<ActionResult> DetailAsync(BaseItem owner)
        => Json(await _details.BuildAsync(owner, Config, HttpContext.RequestAborted).ConfigureAwait(false));
}
