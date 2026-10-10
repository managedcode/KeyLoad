using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record RemoteTransferWireNativeCut(byte[][][][] Owners)
{
    internal static RemoteTransferWireNativeCut Read(PartitionMovementLateNativeOwners owners,
        RemoteTransferColdSeed seed, CancellationToken token)
        => new(owners.Nodes.Select(node => node.Partition.Database.Store.Read(view =>
        {
            var inventory = new RemoteTransferWireRecordInventory(node.Partition.Database, token);
            inventory.Read(view, seed.Scenario.SourcePartition);
            inventory.Read(view, seed.Scenario.DestinationPartition);
            return inventory.Rows;
        })).ToArray());

    internal async Task RequireAsync(RemoteTransferWireNativeCut actual)
    {
        await Assert.That(actual.Owners.Length).IsEqualTo(Owners.Length);
        for (var owner = 0; owner < Owners.Length; owner++)
        {
            await Assert.That(actual.Owners[owner].Length).IsEqualTo(Owners[owner].Length);
            for (var row = 0; row < Owners[owner].Length; row++)
            { await RequireRowAsync(Owners[owner][row], actual.Owners[owner][row]); }
        }
    }

    private static async Task RequireRowAsync(byte[][] original, byte[][] actual)
    {
        await Assert.That(actual.Length).IsEqualTo(original.Length);
        for (var part = 0; part < original.Length; part++)
        { await Assert.That(actual[part].AsSpan().SequenceEqual(original[part])).IsTrue(); }
    }
}
