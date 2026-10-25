using System.Text.Json.Nodes;
using KeyLoad.Clustering;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.UnitTests.Authorization;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.OrleansExecution;

internal static class ConfigureResourceOriginalLifetimeAssertions
{
    internal static ResourceLifetime Lifetime(TestDatabase fixture, ResourceDefinition definition)
        => fixture.Store.Read(view => (ResourceLifetimeCatalog.Read(view, fixture.Partition.TenantId,
            fixture.Partition.DatabaseId, definition.Name, definition)).Value)
            ?? throw new InvalidOperationException("The configured resource has no actual persisted lifetime.");

    internal static byte[] Outcome(TestDatabase fixture, Command<DatabaseCommand> operation)
    {
        // Both tested catalog commands have the original global outcome scope.
        var key = OutcomeStoreOracle.GlobalKey(operation.UserId!, operation.CommandId);
        return fixture.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The original configure outcome is absent.");
    }

    internal static async Task BoundAsync(RequestWorkLease lease, ResourceLifetime actual)
    {
        await Assert.That(lease.Dependencies.Count).IsEqualTo(1);
        await Assert.That(lease.Dependencies.SetEquals([actual])).IsTrue();
        lease.AdmitPublication();
    }

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(JsonDefaults.Serialize(expected)),
            JsonNode.Parse(JsonDefaults.Serialize(actual)))).IsTrue();

    internal static async Task UnchangedAsync(TestDatabase fixture, Func<Task> read)
    {
        var image = TeamRowPolicyAssertions.Image(fixture.Store);
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        var position = fixture.Store.Position;
        var applied = fixture.Database.LastApplied;
        var journal = fixture.ReadCanonicalCut();
        var outbox = fixture.Database.GetOutboxStatus("root", fixture.Partition);
        await read();
        await Assert.That(TeamRowPolicyAssertions.Image(fixture.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(OrleansSerialization.Serialize(fixture.Store.Identity).SequenceEqual(identity)).IsTrue();
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(fixture.Database.LastApplied).IsEqualTo(applied);
        await Assert.That(fixture.ReadCanonicalCut()).IsEqualTo(journal);
        await EqualAsync(outbox, fixture.Database.GetOutboxStatus("root", fixture.Partition));
    }
}
