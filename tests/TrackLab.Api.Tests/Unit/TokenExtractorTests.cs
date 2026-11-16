using TrackLab.Api.Tests.Support;
using TrackLab.Api.Upstream;

namespace TrackLab.Api.Tests.Unit;

public class TokenExtractorTests
{
    /// <summary>Same shape as the real POST /auth/login response.</summary>
    private static string LoginBody(string access, string refresh) => $$"""
        {
          "data": {
            "token": { "type": "Bearer", "access": "{{access}}", "refresh": "{{refresh}}", "expiresIn": 3600 },
            "id": "18b1ff12-4dfd-4403-86b7-b72c991b10f9",
            "username": "assessment.it",
            "appCode": "ASSESSMENT",
            "moduleCode": "ALL"
          },
          "success": true,
          "message": "OK",
          "code": 200
        }
        """;

    [Fact]
    public void Finds_access_and_refresh_tokens_in_the_login_response()
    {
        var access = Jwt.Create(DateTimeOffset.UtcNow.AddHours(1));
        var refresh = Jwt.Create(DateTimeOffset.UtcNow.AddHours(12));

        var body = LoginBody(access, refresh);

        Assert.Equal(access, TokenExtractor.FindAccessToken(body));
        Assert.Equal(refresh, TokenExtractor.FindRefreshToken(body));
    }

    [Fact]
    public void Falls_back_to_other_common_token_names()
    {
        Assert.Equal("abc", TokenExtractor.FindAccessToken("""{"data":{"accessToken":"abc"}}"""));
        Assert.Equal("xyz", TokenExtractor.FindAccessToken("""{"token":"xyz"}"""));
        Assert.Null(TokenExtractor.FindAccessToken("""{"data":{}}"""));
        Assert.Null(TokenExtractor.FindAccessToken("not json"));
    }

    [Fact]
    public void Reads_the_expiry_of_a_jwt()
    {
        var expiry = DateTimeOffset.FromUnixTimeSeconds(1794814243);

        Assert.Equal<DateTimeOffset?>(expiry, TokenExtractor.ReadExpiry(Jwt.Create(expiry)));
        Assert.Null(TokenExtractor.ReadExpiry("not-a-jwt"));
    }

    [Fact]
    public void Merges_set_cookie_headers_into_a_cookie_jar()
    {
        var jar = TokenExtractor.MergeCookieJar(
            "sl-session=old; TS01abd254=lb",
            ["RefreshToken=r1; path=/; httponly", "sl-session=new; path=/; secure"]);

        Assert.Equal("TS01abd254=lb; RefreshToken=r1; sl-session=new", jar);
    }

    [Fact]
    public void A_cookie_set_to_an_empty_value_is_removed_from_the_jar()
    {
        Assert.Equal("a=1", TokenExtractor.MergeCookieJar("a=1; b=2", ["b=; expires=Thu, 01 Jan 1970 00:00:00 GMT"]));
    }

    [Fact]
    public void EnsureCookie_keeps_the_api_cookie_and_adds_a_missing_one()
    {
        Assert.Equal("RefreshToken=from-api", TokenExtractor.EnsureCookie("RefreshToken=from-api", "RefreshToken", "from-body"));
        Assert.Equal("x=1; RefreshToken=from-body", TokenExtractor.EnsureCookie("x=1", "RefreshToken", "from-body"));
        Assert.Equal("x=1", TokenExtractor.EnsureCookie("x=1", "RefreshToken", null));
    }

    [Fact]
    public void Reads_a_display_name_claim_when_the_token_has_one()
    {
        Assert.Equal("Panggalih", TokenExtractor.ReadDisplayName(Jwt.Create(DateTimeOffset.UtcNow.AddHours(1), name: "Panggalih")));
        Assert.Null(TokenExtractor.ReadDisplayName(Jwt.Create(DateTimeOffset.UtcNow.AddHours(1))));
    }
}
