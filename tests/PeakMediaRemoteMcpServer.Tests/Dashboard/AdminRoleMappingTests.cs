namespace PeakMediaRemoteMcpServer.Tests.Dashboard;

// Supports TOR-08-XOxyf4r and TOR-08-iCZ63da: who the dashboard treats as an administrator.
public class AdminRoleMappingTests
{
    [Theory]
    [InlineData(new[] { "Everyone", "PeakMedia Admins" }, "PeakMedia Admins", true)]
    [InlineData(new[] { "Everyone" }, "PeakMedia Admins", false)]
    [InlineData(new[] { "peakmedia admins" }, "PeakMedia Admins", false)]
    [InlineData(new[] { "PeakMedia Admins" }, "", false)]
    [InlineData(new string[0], "PeakMedia Admins", false)]
    public void Admin_role_needs_the_configured_admin_group(string[] groups, string adminGroup, bool expected)
    {
        Assert.Equal(expected, AdminGroup.Grants(groups, adminGroup));
    }

    [Fact]
    public void Session_principal_carries_the_role_the_access_rule_reads()
    {
        Assert.True(AccessRule.IsAdmin(DashboardAuthSetup.SessionPrincipal("00uada", "ada@example.com", isAdmin: true)));
        Assert.False(AccessRule.IsAdmin(DashboardAuthSetup.SessionPrincipal("00upat", "pat@example.com", isAdmin: false)));
        var caller = Caller.From(DashboardAuthSetup.SessionPrincipal("00upat", "pat@example.com", isAdmin: false));
        Assert.Equal(new Caller("00upat", "pat@example.com", false), caller.Value);
    }
}
