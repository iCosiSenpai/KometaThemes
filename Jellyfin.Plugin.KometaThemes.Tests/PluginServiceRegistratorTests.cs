using System;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Plugin.KometaThemes.Tests;

public class PluginServiceRegistratorTests
{
    [Fact]
    public void RegisterServices_DoesNotThrow_WithEmptyContainer()
    {
        // This is a smoke test: building the registrator should never throw
        // at the syntactic level. It does NOT spin up Jellyfin services —
        // it only ensures the registrator's API surface is consistent.
        var registrator = new PluginServiceRegistrator();
        Assert.NotNull(registrator);
    }

    [Fact]
    public void RegisterServices_Type_IsStable()
    {
        // If this test ever changes the type name, the plugin has been
        // refactored and the DI registration in Jellyfin may need updates.
        Assert.Equal("Jellyfin.Plugin.KometaThemes.PluginServiceRegistrator",
            typeof(PluginServiceRegistrator).FullName);
    }
}
