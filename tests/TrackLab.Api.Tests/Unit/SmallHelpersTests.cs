using TrackLab.Api.Configuration;
using TrackLab.Api.Logging;

namespace TrackLab.Api.Tests.Unit;

public class DotEnvTests
{
    [Fact]
    public void Parses_keys_values_quotes_comments_and_export()
    {
        var values = DotEnv.Parse(
        [
            "# comment",
            "",
            "UpstreamApi__AppId=example-app-id",
            "export UpstreamApi__AppKey=\"secret value\"",
            "SINGLE='quoted'",
            "not a pair",
        ]);

        Assert.Equal("example-app-id", values["UpstreamApi__AppId"]);
        Assert.Equal("secret value", values["UpstreamApi__AppKey"]);
        Assert.Equal("quoted", values["SINGLE"]);
        Assert.Equal(3, values.Count);
    }
}

public class CorrelationIdTests
{
    [Theory]
    [InlineData("0f8fad5bd9cb469fa16570867728950e", true)]
    [InlineData("request-12345678", true)]
    [InlineData("short", false)]
    [InlineData("has spaces in it", false)]
    [InlineData("line\nbreak-123456", false)]
    [InlineData(null, false)]
    public void Accepts_only_short_plain_ids(string? value, bool expected) =>
        Assert.Equal(expected, CorrelationIdMiddleware.IsValid(value));
}
