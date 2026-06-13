using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Web;

/// <summary>
/// Registers an index.html transformation with the File Transformation plugin so the
/// ♪ Theme Finder button is injected into the web client automatically — no manual
/// JavaScript-injector configuration needed. Degrades gracefully when File
/// Transformation is not installed.
/// </summary>
public sealed class WebButtonInjectionRegistrar : IHostedService
{
    // Stable id so re-registration across restarts replaces rather than duplicates.
    private const string TransformationId = "b2f6c1a4-6e2d-4a8e-9c1f-4b7e0d2a51c7";
    private const string FileTransformationAssembly = "Jellyfin.Plugin.FileTransformation";

    private readonly ILogger<WebButtonInjectionRegistrar> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebButtonInjectionRegistrar"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public WebButtonInjectionRegistrar(ILogger<WebButtonInjectionRegistrar> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Fire-and-forget with retries: File Transformation may finish initializing
        // its ServiceProvider slightly after we start.
        _ = Task.Run(() => RegisterWithRetriesAsync(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RegisterWithRetriesAsync(CancellationToken cancellationToken)
    {
        const int maxAttempts = 10;
        for (var attempt = 1; attempt <= maxAttempts && !cancellationToken.IsCancellationRequested; attempt++)
        {
            try
            {
                if (TryRegister())
                {
                    _logger.LogInformation("Registered the item-button injection with the File Transformation plugin.");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Item-button injection registration attempt {Attempt} failed; retrying", attempt);
            }

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "File Transformation plugin not detected — the ♪ Theme Finder button will not be auto-injected. "
            + "Install the 'File Transformation' plugin, or manually add /Plugins/KometaThemes/ItemButton.js via a JavaScript injector.");
    }

    private bool TryRegister()
    {
        var assembly = AssemblyLoadContext.All
            .SelectMany(context => context.Assemblies)
            .FirstOrDefault(a => string.Equals(a.GetName().Name, FileTransformationAssembly, StringComparison.Ordinal));

        if (assembly == null)
        {
            return false;
        }

        var pluginInterface = assembly.GetType($"{FileTransformationAssembly}.PluginInterface");
        var register = pluginInterface?.GetMethod("RegisterTransformation", BindingFlags.Public | BindingFlags.Static);
        if (register == null)
        {
            return false;
        }

        // RegisterTransformation takes a Newtonsoft JObject. Build it with File
        // Transformation's own Newtonsoft assembly (via the parameter type) so there
        // is no type-identity mismatch and we don't take a Newtonsoft dependency.
        var jobjectType = register.GetParameters()[0].ParameterType;
        var parse = jobjectType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string) });
        if (parse == null)
        {
            return false;
        }

        // Must be exactly "index.html" — the same key other web-mod plugins
        // (Jellyfin Enhanced, KefinTweaks, …) use. File Transformation groups
        // transformations by their pattern string and, for a request to
        // "index.html", looks up that exact key first and runs ONLY that
        // pipeline. Registering under any other pattern (e.g. an anchored regex)
        // lands us in a separate pipeline that never runs when those plugins are
        // present. The InjectButton endpoint guards against non-HTML (chunk
        // files this loose pattern also matches), so it stays safe.
        var payload = string.Format(
            CultureInfo.InvariantCulture,
            "{{\"id\":\"{0}\",\"fileNamePattern\":\"index.html\",\"transformationEndpoint\":\"/Plugins/KometaThemes/InjectButton\"}}",
            TransformationId);

        var jobject = parse.Invoke(null, new object[] { payload });
        register.Invoke(null, new[] { jobject });
        return true;
    }
}
