using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.KometaThemes.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.KometaThemes;

/// <summary>
/// KometaThemes: anime openings and endings on Jellyfin's series, season and movie pages.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>Name of the plugin page.</summary>
    public const string PageName = "KometaThemes";

    private static readonly object ConfigurationWriteLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Application paths.</param>
    /// <param name="xmlSerializer">XML serializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);
        Instance = this;

        lock (ConfigurationWriteLock)
        {
            if (ConfigurationMigrator.Migrate(Configuration))
            {
                SaveConfiguration();
            }
        }

        PluginVersions.SupersedeOlderCopies(applicationPaths.PluginsPath, Id, Version, Path.GetDirectoryName(AssemblyFilePath));
    }

    /// <inheritdoc />
    public override string Name => "KometaThemes";

    /// <inheritdoc />
    public override string Description => "Anime openings and endings from animethemes.moe on series, season and movie pages.";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("48c98707-45d1-43ac-94b8-f74d875ad29c");

    /// <summary>Gets the running instance.</summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Changes the configuration and saves it, under one lock shared by every writer.
    /// </summary>
    /// <remarks>
    /// The configuration is one object shared by every request. Without the lock two pages saving at
    /// once could lose a change, and the serializer could walk a list another thread was changing.
    /// </remarks>
    /// <param name="mutate">The change. Runs under the lock; do no I/O in it.</param>
    /// <returns>False when the plugin is not loaded.</returns>
    public static bool MutateConfiguration(Action<PluginConfiguration> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        var plugin = Instance;
        if (plugin == null)
        {
            return false;
        }

        lock (ConfigurationWriteLock)
        {
            mutate(plugin.Configuration);
            plugin.SaveConfiguration();
        }

        return true;
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = typeof(Plugin).Namespace;
        return
        [
            new PluginPageInfo
            {
                Name = PageName,
                DisplayName = "Anime themes",
                EmbeddedResourcePath = ns + ".Configuration.configPage.html",
                EnableInMainMenu = true,
                MenuIcon = "music_note",
            },
            new PluginPageInfo { Name = "KometaThemesJs", EmbeddedResourcePath = ns + ".Web.kometa.js" },
            new PluginPageInfo { Name = "KometaThemesCss", EmbeddedResourcePath = ns + ".Web.kometa.css" },
            new PluginPageInfo { Name = "KometaThemesIcon", EmbeddedResourcePath = ns + ".Web.assets.kometathemes-icon.png" },
        ];
    }
}
