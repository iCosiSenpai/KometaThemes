using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Web;

/// <summary>
/// Payload File Transformation passes to <see cref="IndexInjection.TransformIndex"/>.
/// </summary>
public sealed class IndexPayload
{
    /// <summary>Gets or sets the file content.</summary>
    [JsonPropertyName("contents")]
    public string? Contents { get; set; }
}

/// <summary>
/// Adds the ♪ button script to Jellyfin's web client through the File Transformation plugin.
/// </summary>
/// <remarks>
/// 1.x registered an HTTP endpoint for File Transformation to call. That endpoint had to be anonymous
/// and returned whatever HTML it was sent. File Transformation 3 calls a static method in-process
/// instead, so the endpoint is gone.
/// </remarks>
public sealed class IndexInjection : IHostedService
{
    /// <summary>Marker that tells an already transformed page.</summary>
    internal const string Marker = "kometathemes-item-button";

    private const string TransformationId = "b2f6c1a4-6e2d-4a8e-9c1f-4b7e0d2a51c7";
    private const string FileTransformationAssembly = "Jellyfin.Plugin.FileTransformation";

    private readonly ILogger<IndexInjection> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexInjection"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public IndexInjection(ILogger<IndexInjection> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Inserts the script tag before <c>&lt;/body&gt;</c>. Called by File Transformation.
    /// </summary>
    /// <param name="payload">The page.</param>
    /// <returns>The page with the script tag.</returns>
    public static string TransformIndex(IndexPayload payload)
    {
        var contents = payload?.Contents ?? string.Empty;
        if (contents.Length == 0 || contents.Contains(Marker, StringComparison.Ordinal))
        {
            return contents;
        }

        var index = contents.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return contents;
        }

        // Relative to /web/index.html, so it also works behind a base URL such as /jellyfin.
        var version = Plugin.Instance?.Version.ToString() ?? "2";
        var tag = string.Create(CultureInfo.InvariantCulture, $"<script id=\"{Marker}\" defer src=\"../KometaThemes/ItemButton.js?v={version}\"></script>");
        return contents.Insert(index, tag);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() => RegisterAsync(cancellationToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RegisterAsync(CancellationToken cancellationToken)
    {
        // File Transformation may finish loading after this plugin.
        for (var attempt = 0; attempt < 10 && !cancellationToken.IsCancellationRequested; attempt++)
        {
            try
            {
                if (TryRegister())
                {
                    _logger.LogInformation("The theme button is added to Jellyfin's item pages through File Transformation");
                    return;
                }
            }
            catch (Exception ex) when (ex is TargetInvocationException or InvalidOperationException or ArgumentException or MissingMethodException)
            {
                _logger.LogDebug(ex, "File Transformation registration attempt {Attempt} failed", attempt + 1);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        _logger.LogInformation(
            "File Transformation is not installed, so the theme button is not on item pages. Install it, or load ../KometaThemes/ItemButton.js with a JavaScript injector");
    }

    private static bool TryRegister()
    {
        var assembly = AssemblyLoadContext.All
            .SelectMany(context => context.Assemblies)
            .FirstOrDefault(a => string.Equals(a.GetName().Name, FileTransformationAssembly, StringComparison.Ordinal));
        var register = assembly?.GetType(FileTransformationAssembly + ".PluginInterface")?.GetMethod("RegisterTransformation", BindingFlags.Public | BindingFlags.Static);
        if (register == null)
        {
            return false;
        }

        // RegisterTransformation takes File Transformation's own Newtonsoft JObject; build it through
        // the parameter type so no Newtonsoft dependency or type mismatch comes into play.
        var jobject = register.GetParameters()[0].ParameterType;
        var parse = jobject.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);
        if (parse == null)
        {
            return false;
        }

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            id = TransformationId,
            fileNamePattern = "index.html",
            callbackAssembly = typeof(IndexInjection).Assembly.FullName,
            callbackClass = typeof(IndexInjection).FullName,
            callbackMethod = nameof(TransformIndex),
        });

        register.Invoke(null, [parse.Invoke(null, [payload])]);
        return true;
    }
}
