using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.KometaThemes.Web;

/// <summary>
/// Serves the script that adds the ♪ button to item pages.
/// </summary>
/// <remarks>
/// Anonymous on purpose: the web client loads it with the page shell, before any session exists. It
/// only returns a fixed embedded file, and the script itself does nothing for non-administrators.
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("KometaThemes/ItemButton.js")]
public sealed class ItemButtonController : ControllerBase
{
    /// <summary>
    /// Gets the script.
    /// </summary>
    /// <returns>The script.</returns>
    [HttpGet]
    [Produces("application/javascript")]
    public IActionResult Get()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(Plugin).Namespace + ".Web.item-button.js");
        if (stream == null)
        {
            return NotFound();
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        Response.Headers.CacheControl = "public, max-age=3600";
        return Content(reader.ReadToEnd(), "application/javascript", Encoding.UTF8);
    }
}
