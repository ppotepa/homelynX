using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;

namespace TorrentBot.Acl;

public sealed class AclService
{
    private readonly IReadOnlyDictionary<string, string> _users;
    private readonly IReadOnlyDictionary<string, string> _presets;

    public AclService(IReadOnlyDictionary<string, string>? users = null, IReadOnlyDictionary<string, string>? presets = null)
    {
        _users = users ?? DefaultUsers();
        _presets = presets ?? AclPresets.BuiltIn;
    }

    public static AclService FromEnvironment()
    {
        var users = new Dictionary<string, string>(DefaultUsers(), StringComparer.Ordinal);
        var aclFile = Environment.GetEnvironmentVariable("TORRENTBOT_ACL_FILE")
            ?? Environment.GetEnvironmentVariable("TELEGRAM_ALLOWED_USERS_FILE");
        if (!string.IsNullOrWhiteSpace(aclFile) && File.Exists(aclFile))
        {
            foreach (var (subject, expression) in AclParser.ParseFile(File.ReadAllLines(aclFile)))
            {
                users[subject] = expression;
            }
        }

        return new AclService(users);
    }

    private static Dictionary<string, string> DefaultUsers() => new(StringComparer.Ordinal)
    {
        ["cli-user"] = "USER|ALL",
        ["admin"] = "ADMIN",
        ["guest"] = "PUBLIC"
    };

    public UserContext ResolveUser(string userId)
    {
        var grants = _users.TryGetValue(userId, out var grant) ? grant : "USER";
        var expanded = AclPresets.Expand(grants, _presets);
        return new UserContext(userId, expanded.Split('|'), expanded);
    }

    public bool Allows(UserContext user, CapabilityMetadata capability) =>
        AclMatcher.Allows(user.EffectiveProfile, capability);

    public IReadOnlyList<CapabilityMetadata> FilterCapabilities(
        UserContext user,
        IEnumerable<CapabilityMetadata> capabilities) =>
        capabilities.Where(c => Allows(user, c)).ToList();
}