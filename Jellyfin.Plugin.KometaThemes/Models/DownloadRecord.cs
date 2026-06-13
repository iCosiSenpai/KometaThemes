using System;

#pragma warning disable SA1516, CS1591

namespace Jellyfin.Plugin.KometaThemes.Models;

public sealed class DownloadRecord
{
    public int ThemeId { get; set; }
    public ThemeType Type { get; set; }
    public int Sequence { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int SeasonNumber { get; set; }
    public DateTime DownloadedAt { get; set; } = DateTime.UtcNow;
    public Guid ItemId { get; set; }
}
