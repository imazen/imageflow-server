using Imageflow.Server.Configuration.Parsing;
using Xunit;

namespace Imageflow.Server.Configuration.Tests;

public class InvalidTomlConfigurationTests
{
    private readonly ITestOutputHelper _output;

    public InvalidTomlConfigurationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Skip = "Route expression validation not yet wired up to TOML parser")]
    public void TestInvalidRouteWithOptionalSegmentSurroundedBySlashes()
    {
        var toml = """
[imageflow_server]
config_schema = '1.0'
[license]
enforcement = "watermark"
[[routes]]
route = "'/a/{b?}/c' => '/d'"
""";
        var context = new TomlParserContext(DeploymentEnvironment.Production, new(), _ => null, new TestFileMethods(new()));
        var exception = Assert.Throws<InvalidConfigPropertyException>(() => TomlParser.Parse(toml, "invalid.toml", context));

        _output.WriteLine(exception.Message);
        Assert.Contains("An optional segment cannot be surrounded by slashes", exception.Message);
    }
}
