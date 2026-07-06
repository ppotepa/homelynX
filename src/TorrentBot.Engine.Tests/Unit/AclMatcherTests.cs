using TorrentBot.Acl;
using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class AclMatcherTests
{
    [Fact]
    public void Deny_wins_over_grant()
    {
        var cap = new CapabilityMetadata("torrent.delete", "/delete", "", "USER", RiskLevel.Destructive);
        Assert.False(AclMatcher.Allows("USER|!torrent.delete", cap));
        Assert.True(AclMatcher.Allows("ADMIN", cap));
    }
}