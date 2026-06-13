using Jellyfin.Plugin.KometaThemes.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.KometaThemes.Tests;

public class WebInjectionTests
{
    private static string Inject(string html)
    {
        var controller = new KometaWebInjectionController(NullLogger<KometaWebInjectionController>.Instance);
        var result = controller.Inject(new WebTransformationRequest { Contents = html });
        return Assert.IsType<ContentResult>(result).Content ?? string.Empty;
    }

    [Fact]
    public void Inject_AddsScriptTag_BeforeBodyClose()
    {
        var html = "<html><head></head><body><div id=\"app\"></div></body></html>";
        var result = Inject(html);

        Assert.Contains("kometathemes-itembutton", result);
        Assert.Contains("/Plugins/KometaThemes/ItemButton.js", result);
        // injected before the closing body tag
        Assert.True(result.IndexOf("kometathemes-itembutton", System.StringComparison.Ordinal)
                    < result.IndexOf("</body>", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Inject_IsIdempotent()
    {
        var html = "<html><body></body></html>";
        var once = Inject(html);
        var controller = new KometaWebInjectionController(NullLogger<KometaWebInjectionController>.Instance);
        var twice = Assert.IsType<ContentResult>(
            controller.Inject(new WebTransformationRequest { Contents = once })).Content ?? string.Empty;

        var first = twice.IndexOf("kometathemes-itembutton", System.StringComparison.Ordinal);
        var last = twice.LastIndexOf("kometathemes-itembutton", System.StringComparison.Ordinal);
        Assert.Equal(first, last); // exactly one occurrence
    }

    [Fact]
    public void Inject_EmptyContents_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, Inject(string.Empty));
    }

    [Fact]
    public void Inject_NoBodyTag_LeavesContentUnchanged()
    {
        // A mis-matched non-HTML file (e.g. a JS chunk) must never be modified.
        const string js = "(function(){console.log('chunk');})();";
        Assert.Equal(js, Inject(js));
    }
}
