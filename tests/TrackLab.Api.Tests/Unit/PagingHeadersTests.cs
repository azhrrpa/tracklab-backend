using System.Text;
using TrackLab.Api.Contracts;
using TrackLab.Api.Upstream;

namespace TrackLab.Api.Tests.Unit;

public class PagingHeadersTests
{
    [Fact]
    public void Maps_simple_paging_to_x_paging_headers()
    {
        var headers = PagingHeaders.FromSimple(new PagingQuery
        {
            Offset = 20,
            Limit = 10,
            SortBy = "code",
            SortOrder = "desc",
            Search = " REQ-CREATE ",
        });

        Assert.Equal("20", headers["X-Paging-Offset"]);
        Assert.Equal("10", headers["X-Paging-Limit"]);
        Assert.Equal("code", headers["X-Paging-SortBy"]);
        Assert.Equal("DESC", headers["X-Paging-SortOrder"]);
        Assert.Equal("REQ-CREATE", headers["X-Paging-Search"]);
    }

    [Fact]
    public void Always_sends_a_sort_column_because_the_api_fails_without_one()
    {
        var headers = PagingHeaders.FromSimple(new PagingQuery());

        Assert.Equal("id", headers["X-Paging-SortBy"]);
        Assert.False(headers.ContainsKey("X-Paging-Search"));
    }

    [Fact]
    public void Base64_encodes_advanced_filters_like_the_documentation_example()
    {
        var headers = PagingHeaders.FromAdvanced(new AdvancedPagingQuery
        {
            Operator = "or",
            IgnoreCase = true,
            Include = """{"tracker_code":"REQ-ACC||REQ-CREATE"}""",
            Exclude = """{ "tracker_code" : "REQ-CANCEL" }""",
        });

        Assert.Equal("OR", headers["X-Paging-Operator"]);
        Assert.Equal("true", headers["X-Paging-IgnoreCase"]);
        Assert.Equal("""{"tracker_code":"REQ-ACC||REQ-CREATE"}""", Decode(headers["X-Paging-Include"]));
        Assert.Equal("""{"tracker_code":"REQ-CANCEL"}""", Decode(headers["X-Paging-Exclude"]));
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("""{"code":1}""")]
    [InlineData("{not json")]
    public void Rejects_filters_that_are_not_a_flat_object_of_strings(string filter)
    {
        Assert.False(PagingHeaders.TryNormalizeFilter(filter, out _, out var error));
        Assert.NotEmpty(error);
    }

    private static string Decode(string base64) => Encoding.UTF8.GetString(Convert.FromBase64String(base64));
}
