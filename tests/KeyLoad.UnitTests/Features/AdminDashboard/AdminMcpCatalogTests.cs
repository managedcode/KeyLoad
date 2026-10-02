using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.AdminDashboard;

internal sealed class AdminMcpCatalogTests
{
    [Test]
    public async Task AcAd007OfficialCatalogIncludesExactlyThreeReadOnlyNonDestructiveAdminTools()
    {
        var entries = AdminDashboardMcpCatalog.Entries;
        await Assert.That(entries.Length).IsEqualTo(3);
        await Assert.That(entries.Select(entry => entry.Name).SequenceEqual(new[]
        { AdminDashboardProtocol.SnapshotTool, AdminDashboardProtocol.ResourcesTool, AdminDashboardProtocol.QueueTool })).IsTrue();
        await Assert.That(entries.Select(entry => entry.ReadKind).SequenceEqual(new GrainReadKind?[]
        { GrainReadKind.AdminDashboard, GrainReadKind.AdminResources, GrainReadKind.AdminQueue })).IsTrue();
        foreach (var entry in entries)
        {
            await Assert.That(McpOperationCatalog.TryGet(entry.Name, out var published)).IsTrue();
            await Assert.That(published!.ReadOnly).IsTrue();
            await Assert.That(published.Destructive).IsFalse();
            await Assert.That(published.Idempotent).IsTrue();
            await Assert.That(published.CommandKind).IsNull();
        }
    }
}
