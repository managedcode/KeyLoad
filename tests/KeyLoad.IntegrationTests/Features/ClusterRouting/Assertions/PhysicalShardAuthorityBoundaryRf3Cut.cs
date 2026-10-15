using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardAuthorityBoundaryRf3Cut
{
    private const int FirstEntry = 0;
    private const int FirstUnstarted = 1;
    internal static async Task<PartitionMovementPublicParentRf3NativeCut[][]> CaptureAndReopenAsync(
        TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed[] seeds, CancellationToken token)
    {
        var result = await PhysicalShardPartitionMovementRf3Cold.StopAndReadAsync(wave,
            seeds.Select(seed => seed.FirstRequest).ToArray(), token);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        foreach (var seed in seeds)
        {
            await seed.ReconnectAsync(wave, token);
        }
        return result;
    }

    internal static async Task RequireAndReopenAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed[] seeds, PartitionMovementPublicParentRf3NativeCut[][] expected,
        CancellationToken token)
    {
        var actual = await CaptureAndReopenAsync(wave, seeds, token);
        for (var index = FirstEntry; index < seeds.Length; index++)
        {
            foreach (var cut in actual[index])
            {
                var previous = expected[index].Single(value => value.Node == cut.Node);
                await SqlRf3Protocol.EqualAsync(DomainRows(previous, seeds[index].Partition), DomainRows(cut, seeds[index].Partition));
                await SqlRf3Protocol.EqualAsync(previous.Header, cut.Header);
                await SqlRf3Protocol.EqualAsync(previous.Pending, cut.Pending);
                await SqlRf3Protocol.EqualAsync(previous.LastIssued, cut.LastIssued);
                if (index >= FirstUnstarted)
                {
                    await Assert.That(cut.Header).IsNull();
                    await Assert.That(cut.Pending).IsNull();
                    await Assert.That(cut.LastIssued).IsNull();
                }
            }
        }
    }

    private static string[] DomainRows(PartitionMovementPublicParentRf3NativeCut cut, PartitionRef partition)
    {
        var prefixes = PartitionRecordFamilies.All.Select(family => Convert.ToHexString(KeySpace.Partition(family, partition))).ToArray();
        return cut.Rows.Where(row => prefixes.Any(prefix => row.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
    }
}
