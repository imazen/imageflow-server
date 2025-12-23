using Imazen.Routing.Serving;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Imazen.Tests.Routing.Serving;

public class ServiceCollectionMultipleInterfacesExtensionsTests
{
    // Test interfaces
    private interface IFirst { string Name { get; } }
    private interface ISecond { int Value { get; } }
    private interface IThird { bool Flag { get; } }
    private interface IFourth { double Score { get; } }

    // Implementation that implements all four interfaces
    private class MultiInterfaceService : IFirst, ISecond, IThird, IFourth
    {
        public string Name => "TestService";
        public int Value => 42;
        public bool Flag => true;
        public double Score => 3.14;
    }

    [Fact]
    public void RegisterSingletonByTwoInterfaces_ResolvesBothInterfaces()
    {
        // Arrange
        var services = new ServiceCollection();
        services.RegisterSingletonByTwoInterfaces<IFirst, ISecond>(
            _ => new MultiInterfaceService());

        var provider = services.BuildServiceProvider();

        // Act
        var first = provider.GetRequiredService<IFirst>();
        var second = provider.GetRequiredService<ISecond>();

        // Assert
        Assert.Equal("TestService", first.Name);
        Assert.Equal(42, second.Value);
    }

    [Fact]
    public void RegisterSingletonByTwoInterfaces_ReturnsSameInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.RegisterSingletonByTwoInterfaces<IFirst, ISecond>(
            _ => new MultiInterfaceService());

        var provider = services.BuildServiceProvider();

        // Act
        var first = provider.GetRequiredService<IFirst>();
        var second = provider.GetRequiredService<ISecond>();

        // Assert - both should be the same instance
        Assert.Same(first, second);
    }

    [Fact]
    public void RegisterSingletonByThreeInterfaces_ResolvesAllThreeInterfaces()
    {
        // Arrange
        var services = new ServiceCollection();
        services.RegisterSingletonByThreeInterfaces<IFirst, ISecond, IThird>(
            _ => new MultiInterfaceService());

        var provider = services.BuildServiceProvider();

        // Act
        var first = provider.GetRequiredService<IFirst>();
        var second = provider.GetRequiredService<ISecond>();
        var third = provider.GetRequiredService<IThird>();

        // Assert
        Assert.Equal("TestService", first.Name);
        Assert.Equal(42, second.Value);
        Assert.True(third.Flag);
    }

    [Fact]
    public void RegisterSingletonByThreeInterfaces_ReturnsSameInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.RegisterSingletonByThreeInterfaces<IFirst, ISecond, IThird>(
            _ => new MultiInterfaceService());

        var provider = services.BuildServiceProvider();

        // Act
        var first = provider.GetRequiredService<IFirst>();
        var second = provider.GetRequiredService<ISecond>();
        var third = provider.GetRequiredService<IThird>();

        // Assert - all should be the same instance
        Assert.Same(first, second);
        Assert.Same(second, third);
    }

    [Fact]
    public void RegisterSingletonUnderFourInterfaces_ResolvesAllFourInterfaces()
    {
        // Arrange
        var services = new ServiceCollection();
        services.RegisterSingletonUnderFourInterfaces<IFirst, ISecond, IThird, IFourth>(
            _ => new MultiInterfaceService());

        var provider = services.BuildServiceProvider();

        // Act
        var first = provider.GetRequiredService<IFirst>();
        var second = provider.GetRequiredService<ISecond>();
        var third = provider.GetRequiredService<IThird>();
        var fourth = provider.GetRequiredService<IFourth>();

        // Assert
        Assert.Equal("TestService", first.Name);
        Assert.Equal(42, second.Value);
        Assert.True(third.Flag);
        Assert.Equal(3.14, fourth.Score);
    }

    [Fact]
    public void RegisterSingletonUnderFourInterfaces_ReturnsSameInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.RegisterSingletonUnderFourInterfaces<IFirst, ISecond, IThird, IFourth>(
            _ => new MultiInterfaceService());

        var provider = services.BuildServiceProvider();

        // Act
        var first = provider.GetRequiredService<IFirst>();
        var second = provider.GetRequiredService<ISecond>();
        var third = provider.GetRequiredService<IThird>();
        var fourth = provider.GetRequiredService<IFourth>();

        // Assert - all should be the same instance
        Assert.Same(first, second);
        Assert.Same(second, third);
        Assert.Same(third, fourth);
    }

    [Fact]
    public void RegisterSingletonByTwoInterfaces_FactoryReceivesServiceProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<string>("injected-value");

        IServiceProvider? capturedProvider = null;
        services.RegisterSingletonByTwoInterfaces<IFirst, ISecond>(
            provider =>
            {
                capturedProvider = provider;
                return new MultiInterfaceService();
            });

        var provider = services.BuildServiceProvider();

        // Act
        _ = provider.GetRequiredService<IFirst>();

        // Assert
        Assert.NotNull(capturedProvider);
        Assert.Equal("injected-value", capturedProvider!.GetRequiredService<string>());
    }

    // A class that only implements IFirst, not ISecond
    private class OnlyFirstService : IFirst
    {
        public string Name => "OnlyFirst";
    }

    [Fact]
    public void RegisterSingletonByTwoInterfaces_ThrowsWhenTypeDoesNotImplementSecondInterface()
    {
        // Arrange
        var services = new ServiceCollection();

        // This should throw at resolution time because OnlyFirstService doesn't implement ISecond
        services.RegisterSingletonByTwoInterfaces<IFirst, ISecond>(
            _ => new OnlyFirstService());

        var provider = services.BuildServiceProvider();

        // Act & Assert
        Assert.Throws<NotSupportedException>(() => provider.GetRequiredService<IFirst>());
    }
}
