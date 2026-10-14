using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.Search;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

internal static class Bm25PortableMovementRefusals
{
    internal static async Task RunAsync(ControlledPartitionMovementNativeHosts owners,
        Bm25CorpusCurrentV1 current, byte[] original, CancellationToken token)
    {
        var captured = Bm25PortableMovementCapture.Read(owners.Source, token);
        var limits = owners.Target.Database.Limits;
        PartitionMovePageAdmission.Require(captured.Page, captured.Fence, limits, limits.MaxBatchBytes);
        foreach (var fault in new[] { current with { FormatVersion = 0 }, current with { Statistics = null! } })
        {
            await PageAsync(owners, captured, NativeSerialization.Serialize(fault), token);
            await SourceAsync(owners, current, original, NativeSerialization.Serialize(fault), token);
        }
        var repaired = Bm25PortableMovementCapture.Read(owners.Source, token);
        await Assert.That(repaired.Page.Records[0].Value.Span.SequenceEqual(original)).IsTrue();
        PartitionMovePageAdmission.Require(repaired.Page, repaired.Fence, limits, limits.MaxBatchBytes);
    }

    private static async Task PageAsync(ControlledPartitionMovementNativeHosts owners,
        (KeyLoad.Core.Features.ClusterRouting.Contracts.PartitionMoveSourceFenceRecord Fence,
        KeyLoad.Core.Features.ClusterRouting.Contracts.PartitionMoveImagePage Page) original,
        byte[] badBytes, CancellationToken token)
    {
        var rows = original.Page.Records.SetItem(0, new(original.Page.Records[0].Key, badBytes));
        var controlled = original.Page with
        {
            Records = rows,
            Digest = PartitionMoveImageDigest.Records(rows)
        };
        var before = await Bm25PortableMovementCapture.SixAsync(owners, token);
        var refused = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => Task.Run(() =>
            PartitionMovePageAdmission.Require(controlled, original.Fence, owners.Target.Database.Limits,
                owners.Target.Database.Limits.MaxBatchBytes), token));
        await Assert.That(refused!.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(refused.Message).IsEqualTo(Bm25CorpusProtocol.Invalid);
        await Bm25PortableMovementAssertions.UnchangedAsync(before, owners, token);
    }

    private static async Task SourceAsync(ControlledPartitionMovementNativeHosts owners,
        Bm25CorpusCurrentV1 current, byte[] original, byte[] badBytes, CancellationToken token)
    {
        var source = owners.Source;
        var key = Bm25CorpusKeys.Current(current);
        var failures = new List<Exception>();
        var repairRequired = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            repairRequired = true;
            source.Store.Commit((transaction, _) => { transaction.Put(key, badBytes); return true; });
            var before = await Bm25PortableMovementCapture.SixAsync(owners, token);
            var refused = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => Task.Run(() =>
                Bm25PortableMovementCapture.Read(source, token), token));
            await Assert.That(refused!.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(refused.Message).IsEqualTo(Bm25CorpusProtocol.Invalid);
            await Bm25PortableMovementAssertions.UnchangedAsync(before, owners, token);
        }, failures);
        if (repairRequired)
        {
            ServerFailureObserver.Observe(() => source.Store.Commit((transaction, _) =>
                {
                    transaction.Put(key, original);
                    return true;
                }), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await Bm25PortableMovementAssertions.HeadAsync(source, current, original, token);
    }
}
