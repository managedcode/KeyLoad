using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Original native plan refusals and valid ordered-binding changes before original slot continuation.</summary>
internal static class ClusterRestoreRf3PlanBindingsTrial
{
    private const int FirstMapping = 0;
    private const int FirstMember = 0;
    private const int SecondMember = 1;
    private const int RequiredVoters = 3;

    internal static async Task RunAsync(ClusterRestoreRf3Fixture target,
        ImmutableArray<ClusterBackupOwnerReceipt> originals, ImmutableArray<string> archives,
        NativeClusterRestoreStage stage, byte[] originalOperation, CancellationToken token)
    {
        var published = Directory.Exists(target.DataRoot);
        await target.StartRejectedPlanAsync(signer: true, published, token).ConfigureAwait(false);
        await Assert.That(ClusterRestoreRf3RetainedCut.Operation(target).SequenceEqual(originalOperation)).IsTrue();
        await target.StartRejectedPlanAsync(signer: false, published, token).ConfigureAwait(false);
        await Assert.That(ClusterRestoreRf3RetainedCut.Operation(target).SequenceEqual(originalOperation)).IsTrue();
        if (stage != NativeClusterRestoreStage.PlanPublished)
        {
            return;
        }
        var mapping = target.Mappings[FirstMapping];
        await RequireChangedAsync(target, target.Mappings.SetItem(FirstMapping, mapping with
        {
            Target = mapping.Target with
            {
                VoterIds = Exchange(mapping.Target.VoterIds)
            }
        }), originals, archives, token).ConfigureAwait(false);
        await RequireChangedAsync(target, target.Mappings.SetItem(FirstMapping, mapping with
        {
            Endpoints = Exchange(mapping.Endpoints)
        }), originals, archives, token).ConfigureAwait(false);
    }

    private static ImmutableArray<string> Exchange(ImmutableArray<string> original)
    {
        if (original.IsDefault || original.Length != RequiredVoters)
        {
            throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid);
        }
        return original.SetItem(FirstMember, original[SecondMember]).SetItem(SecondMember, original[FirstMember]);
    }

    private static async Task RequireChangedAsync(ClusterRestoreRf3Fixture target,
        ImmutableArray<ClusterRestoreOwnerMapping> mappings, ImmutableArray<ClusterBackupOwnerReceipt> originals,
        ImmutableArray<string> archives, CancellationToken token)
    {
        var plan = ClusterRestoreRf3RetainedCut.Plan(target);
        var operation = ClusterRestoreRf3RetainedCut.Operation(target);
        var published = Directory.Exists(target.DataRoot);
        var image = published ? ClusterRestoreRf3RetainedCut.Target(target) : null;
        await target.StartRejectedPlanAsync(signer: false, published, token, mappings).ConfigureAwait(false);
        await Assert.That(ClusterRestoreRf3RetainedCut.Plan(target).SequenceEqual(plan)).IsTrue();
        await Assert.That(ClusterRestoreRf3RetainedCut.Operation(target).SequenceEqual(operation)).IsTrue();
        await Assert.That(Directory.Exists(target.DataRoot)).IsEqualTo(published);
        await Assert.That(File.Exists(target.DataRoot)).IsFalse();
        if (image is not null)
        {
            await Assert.That(ClusterRestoreRf3RetainedCut.Target(target).SequenceEqual(image)).IsTrue();
        }
        await ClusterRestoreRf3NativeArchive.RequireOriginalAsync(originals, archives, token).ConfigureAwait(false);
    }
}
