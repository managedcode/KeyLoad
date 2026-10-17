using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementCleanupReadBudgetRf3Cold
{
    internal static async Task<(Guid Phase, PartitionMovementPublicParentRf3NativeCut[] Cuts)> CaptureAsync(
        TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        var latest = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token);
        var phase = latest.First(cut => cut.LastIssued is not null).LastIssued!.OriginalPhaseCommandId;
        var exact = ReadAsync(wave, seed, phase);
        return (phase, exact);
    }

    internal static async Task ReplayAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        (Guid Phase, PartitionMovementPublicParentRf3NativeCut[] Cuts) original,
        IReadOnlyList<PartitionMovementPublicParentRf3Seed> seeds, CancellationToken token)
    {
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, original.Phase, token);
        await EqualAsync(original.Cuts, before);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        foreach (var caller in seeds)
        {
            await caller.ReconnectAsync(wave, token);
        }
        await PartitionMovementCleanupReadBudgetRf3Calls.RefusalsAsync(seed, token);
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, original.Phase, token);
        await EqualAsync(before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        foreach (var caller in seeds)
        {
            await caller.ReconnectAsync(wave, token);
        }
    }

    private static PartitionMovementPublicParentRf3NativeCut[] ReadAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, Guid phase)
        => TwoRf3MembershipProtocol.Nodes.Select(node => PartitionMovementPublicParentRf3Cut.ReadStopped(
            wave, node, seed.FirstRequest, phase)).ToArray();

    private static async Task EqualAsync(PartitionMovementPublicParentRf3NativeCut[] expected,
        PartitionMovementPublicParentRf3NativeCut[] actual)
    {
        await Assert.That(string.Equals(JsonSerializer.Serialize(actual), JsonSerializer.Serialize(expected),
            StringComparison.Ordinal)).IsTrue();
    }
}
