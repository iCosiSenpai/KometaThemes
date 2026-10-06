using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// Base of the plugin's controllers: camelCase JSON regardless of the server's own naming policy, and
/// errors as <c>{ "error": "..." }</c> with a sentence the page can show as it is.
/// </summary>
public abstract class KometaControllerBase : ControllerBase
{
    /// <summary>JSON options of every plugin response.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// Returns a value as camelCase JSON.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The result.</returns>
    protected JsonResult Json(object? value) => new(value, JsonOptions);

    /// <summary>
    /// Returns an error the page shows to the owner.
    /// </summary>
    /// <param name="status">HTTP status.</param>
    /// <param name="message">A sentence that says what went wrong and what to do.</param>
    /// <returns>The result.</returns>
    protected JsonResult Error(int status, string message) => new(new { error = message }, JsonOptions) { StatusCode = status };
}
