using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Web;

/// <summary>
/// Request body posted by the File Transformation plugin when transforming a web file.
/// </summary>
public sealed class WebTransformationRequest
{
    /// <summary>
    /// Gets or sets the current file contents.
    /// </summary>
    [JsonPropertyName("contents")]
    public string Contents { get; set; } = string.Empty;
}
