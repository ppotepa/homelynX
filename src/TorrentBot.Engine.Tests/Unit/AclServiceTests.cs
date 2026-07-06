using TorrentBot.Acl;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class AclServiceTests
{
    [Fact]
    public void FromEnvironment_merges_acl_file_users()
    {
        var aclFile = Path.Combine(Path.GetTempPath(), $"torrentbot-acl-{Guid.NewGuid():N}.cfg");
        try
        {
            File.WriteAllLines(aclFile,
            [
                "# test users",
                "custom-user ADMIN",
                "guest USER"
            ]);

            Environment.SetEnvironmentVariable("TORRENTBOT_ACL_FILE", aclFile);
            var acl = AclService.FromEnvironment();
            var custom = acl.ResolveUser("custom-user");
            var guest = acl.ResolveUser("guest");

            Assert.Contains("ADMIN", custom.EffectiveProfile, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("USER", guest.EffectiveProfile, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TORRENTBOT_ACL_FILE", null);
            if (File.Exists(aclFile))
            {
                File.Delete(aclFile);
            }
        }
    }
}