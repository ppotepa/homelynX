using TorrentBot.Acl;
using TorrentBot.Bootstrap;
using TorrentBot.Adapters.Cli;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Engine.Audit;

namespace TorrentBot.Engine.Tests.Integration;

public sealed class AuditPersistenceIntegrationTests
{
    [Fact]
    public async Task Portal_audit_sink_records_capability_execution()
    {
        var audit = PortalAuditSink.CreateInMemory();
        var engine = EngineBootstrap.Create(auditSink: audit);
        await engine.StartAsync();
        try
        {
        var result = await engine.SubmitAsync(new Invocation
        {
            IsExplicit = true,
            CapabilityName = "system.health",
            RequestContext = new RequestContext(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "admin", source: "test"),
            User = new AclService().ResolveUser("admin")
        });

        Assert.True(result.Success);
        Assert.True(audit.CountEvents() > 0);
        Assert.Contains(audit.ListEvents(), r => r.CapabilityName == "system.health" && r.Success);
        }
        finally
        {
            await engine.StopAsync();
        }
    }
}