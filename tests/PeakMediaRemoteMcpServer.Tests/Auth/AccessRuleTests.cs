namespace PeakMediaRemoteMcpServer.Tests.Auth;

public class AccessRuleTests
{
    private static readonly Caller Pat = new("00u-pat", "pat@example.com", IsAdmin: false);
    private static readonly Caller Ada = new("00u-ada", "ada@example.com", IsAdmin: true);

    [Fact]
    public void Owner_can_access_own_record()
    {
        Assert.True(AccessRule.CanAccess(Pat, "00u-pat"));
    }

    [Fact]
    public void Other_user_cannot_access_record()
    {
        Assert.False(AccessRule.CanAccess(Pat, "00u-sam"));
        Assert.True(AccessRule.RequireAdmin(Pat).IsFailed);
    }

    [Fact]
    public void Admin_can_access_any_record()
    {
        Assert.True(AccessRule.CanAccess(Ada, "00u-sam"));
        Assert.True(AccessRule.RequireAdmin(Ada).IsSuccess);
    }

    [Fact]
    public void Principal_without_sub_is_not_a_caller()
    {
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("email", "pat@example.com")], "Bearer"));

        Assert.True(Caller.From(principal).IsFailed);
        Assert.False(AccessRule.IsAdmin(principal));
    }

    [Fact]
    public void Unauthenticated_principal_is_not_a_caller()
    {
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("sub", "00u-pat")]));

        Assert.True(Caller.From(principal).IsFailed);
    }

    [Fact]
    public async Task Groups_are_read_from_a_configured_claim_name()
    {
        await using var stub = await StubOidcIssuer.StartAsync();
        stub.GroupsClaimName = "peakmedia_groups";
        await using var host = await GatewayTestHost.StartAsync(stub, new Dictionary<string, string?>
        {
            ["Broker:UpstreamGroupsClaim"] = "peakmedia_groups",
        });

        var signedIn = await TestSignIn.SignInAsync(host.Client, stub, StubUser.For("ada@example.com", "org.peakmedia.users", TestSignIn.AdminGroup));

        Assert.Equal("admin", new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(signedIn.AccessToken).GetClaim("role").Value);
    }

    [Theory]
    [InlineData(true, "admin")]
    [InlineData(false, "user")]
    public async Task Role_claim_follows_the_admin_group_at_sign_in(bool inAdminGroup, string expectedRole)
    {
        await using var stub = await StubOidcIssuer.StartAsync();
        await using var host = await GatewayTestHost.StartAsync(stub);
        var groups = inAdminGroup ? new[] { "Everyone", TestSignIn.AdminGroup } : new[] { "Everyone" };

        var signedIn = await TestSignIn.SignInAsync(host.Client, stub, StubUser.For("ada@example.com", groups));

        var token = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(signedIn.AccessToken);
        Assert.Equal(expectedRole, token.GetClaim("role").Value);
        Assert.Equal("ada@example.com", token.GetClaim("email").Value);
        Assert.Equal(StubUser.For("ada@example.com").Subject, token.Subject);

        using var refreshed = await TestSignIn.RefreshAsync(host.Client, signedIn.Client, signedIn.RefreshToken);
        using var document = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync());
        var refreshedToken = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(document.RootElement.GetProperty("access_token").GetString());
        Assert.Equal(expectedRole, refreshedToken.GetClaim("role").Value);
    }
}
