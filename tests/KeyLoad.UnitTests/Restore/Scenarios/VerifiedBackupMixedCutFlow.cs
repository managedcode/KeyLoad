using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class VerifiedBackupMixedCutFlow
{
    private const string Target = "mixed-cut-native-target";

    internal static async Task RunAsync(bool earlierJournal, CancellationToken token)
    {
        await EventingArtifactFixture.RunAsync(async fixture =>
        {
            await EventingArtifactSeed.RunAsync(fixture);
            var pair = await VerifiedBackupCutPair.CaptureAsync(fixture, token);
            var selected = earlierJournal ? pair.Later : pair.Earlier;
            var donor = earlierJournal ? pair.Earlier : pair.Later;
            var sourcePosition = fixture.Source.Store.Position;
            var sourceRows = EventingArtifactState.FullBytes(fixture.Source.Store);
            var target = Path.Combine(fixture.Root, Target);
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                using var authority = new RestorePublicationRaceAuthority(fixture, selected.Capture);
                await CompleteAsync(fixture, authority, selected, donor, target, sourcePosition, sourceRows, token);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        });
    }

    private static async Task CompleteAsync(EventingArtifactFixture fixture, RestorePublicationRaceAuthority authority,
        VerifiedBackupCutSnapshot selected, VerifiedBackupCutSnapshot donor, string target,
        long sourcePosition, string[] sourceRows, CancellationToken token)
    {
        IReadOnlyDictionary<string, byte[]>? mixed = null;
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            mixed = await VerifiedBackupMixedCutTransfer.MixAsync(selected, donor, token);
            await VerifiedBackupMixedCutRefusal.RunAsync(fixture, authority, selected, target, token);
        }, failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() =>
            VerifiedBackupMixedCutConservation.RunAsync(fixture, sourcePosition, sourceRows, selected, donor, mixed ?? selected.Archive), failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() =>
            VerifiedBackupMixedCutTransfer.RepairAsync(selected), failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() =>
            VerifiedBackupMixedCutConservation.RunAsync(fixture, sourcePosition, sourceRows, selected, donor, selected.Archive), failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            var identity = authority.Restore(fixture, selected.Path, target, null, token);
            await authority.RequireIdentityAsync(identity, fixture.Source.Store.Identity);
            fixture.OpenTarget(target);
            await Assert.That(fixture.Target!.Position).IsEqualTo(authority.ExpectedPosition);
            await Assert.That(EventingArtifactState.Bytes(fixture.Target, fixture.Source.Partition))
                .IsEquivalentTo(selected.PartitionRows, CollectionOrdering.Matching);
            await VerifiedBackupMixedCutState.StateAsync(fixture, fixture.Database, selected.SecondOperation is not null);
            await VerifiedBackupMixedCutCold.RunAsync(fixture, selected, target, authority.ExpectedPosition, token);
        }, failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() =>
            VerifiedBackupMixedCutConservation.RunAsync(fixture, sourcePosition, sourceRows, selected, donor, selected.Archive), failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
