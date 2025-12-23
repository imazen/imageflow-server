using Imazen.Routing.Layers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Imazen.Tests.Routing.Layers;

public class DiagnosticsPageOptionsTests
{
    [Fact]
    public void AddDiagnosticsPage_BindsConfigurationSection()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Imageflow::Diagnostics:Password"] = "testpassword123",
                ["Imageflow::Diagnostics:AccessFrom"] = "LocalHost"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddDiagnosticsPage();

        var provider = services.BuildServiceProvider();

        // Act
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<DiagnosticsPageOptions>>();
        var options = optionsMonitor.CurrentValue;

        // Assert
        Assert.Equal("testpassword123", options.Password);
        Assert.Equal(DiagnosticsPageOptions.AccessDiagnosticsFrom.LocalHost, options.AccessFrom);
    }

    [Fact]
    public void AddDiagnosticsPage_WithDefaults_UsesDefaultsWhenConfigMissing()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Only set password in config, leave AccessFrom to default
                ["Imageflow::Diagnostics:Password"] = "configpassword"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddDiagnosticsPage(new DiagnosticsPageOptions
        {
            Password = "defaultpassword",
            AccessFrom = DiagnosticsPageOptions.AccessDiagnosticsFrom.AnyHost
        });

        var provider = services.BuildServiceProvider();

        // Act
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<DiagnosticsPageOptions>>();
        var options = optionsMonitor.CurrentValue;

        // Assert - config value should override default for Password
        Assert.Equal("configpassword", options.Password);
        // Default should be used for AccessFrom since not in config
        Assert.Equal(DiagnosticsPageOptions.AccessDiagnosticsFrom.AnyHost, options.AccessFrom);
    }

    [Fact]
    public void AddDiagnosticsPage_ConfigOverridesDefaults()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Imageflow::Diagnostics:Password"] = "configpassword",
                ["Imageflow::Diagnostics:AccessFrom"] = "None"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddDiagnosticsPage(new DiagnosticsPageOptions
        {
            Password = "defaultpassword",
            AccessFrom = DiagnosticsPageOptions.AccessDiagnosticsFrom.AnyHost
        });

        var provider = services.BuildServiceProvider();

        // Act
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<DiagnosticsPageOptions>>();
        var options = optionsMonitor.CurrentValue;

        // Assert - config values should override defaults
        Assert.Equal("configpassword", options.Password);
        Assert.Equal(DiagnosticsPageOptions.AccessDiagnosticsFrom.None, options.AccessFrom);
    }

    [Fact]
    public void AddDiagnosticsPage_NoConfig_UsesDefaults()
    {
        // Arrange - empty configuration
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddDiagnosticsPage(new DiagnosticsPageOptions
        {
            Password = "defaultpassword",
            AccessFrom = DiagnosticsPageOptions.AccessDiagnosticsFrom.LocalHost
        });

        var provider = services.BuildServiceProvider();

        // Act
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<DiagnosticsPageOptions>>();
        var options = optionsMonitor.CurrentValue;

        // Assert - defaults should be used
        Assert.Equal("defaultpassword", options.Password);
        Assert.Equal(DiagnosticsPageOptions.AccessDiagnosticsFrom.LocalHost, options.AccessFrom);
    }
}
