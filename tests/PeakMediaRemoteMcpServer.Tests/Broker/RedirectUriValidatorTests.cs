namespace PeakMediaRemoteMcpServer.Tests.Broker;

public class RedirectUriValidatorTests
{
    [Theory]
    [InlineData("http://127.0.0.1:33418/callback", true)]
    [InlineData("http://localhost/callback", true)]
    [InlineData("http://[::1]:9000/cb", true)]
    [InlineData("https://evil.example.com/cb", false)]
    [InlineData("http://localhost@evil.example.com/cb", false)]
    [InlineData("http://127.0.0.1/cb#frag", false)]
    [InlineData("custom-scheme://localhost/cb", false)]
    public void Registration_accepts_only_loopback_by_default(string uri, bool expected)
    {
        Assert.Equal(expected, RedirectUriValidator.IsAllowedForRegistration(uri, []));
    }

    [Fact]
    public void Authorize_matches_loopback_on_any_port_but_exact_path()
    {
        string[] registered = ["http://127.0.0.1:1111/callback"];

        Assert.True(RedirectUriValidator.MatchesRegistered("http://127.0.0.1:2222/callback", registered));
        Assert.False(RedirectUriValidator.MatchesRegistered("http://127.0.0.1:2222/other", registered));
        Assert.False(RedirectUriValidator.MatchesRegistered("http://localhost:2222/callback", registered));
    }

    [Fact]
    public void Non_loopback_must_match_exactly_including_port()
    {
        string[] registered = ["https://app.example.com/cb"];

        Assert.True(RedirectUriValidator.MatchesRegistered("https://app.example.com/cb", registered));
        Assert.False(RedirectUriValidator.MatchesRegistered("https://app.example.com:8443/cb", registered));
    }
}
