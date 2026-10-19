using KeyLoad.Core;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class VerifiedBackupMixedCutConservation
{
    internal static async Task RunAsync(EventingArtifactFixture fixture, long sourcePosition,
        string[] sourceRows, VerifiedBackupCutSnapshot selected, VerifiedBackupCutSnapshot donor,
        IReadOnlyDictionary<string, byte[]> selectedBytes)
    {
        await SourceAsync(fixture, sourcePosition, sourceRows, selected.SecondOperation is not null ? selected : donor);
        await BackupIndexedDocumentDedupAssertions.AssertArchiveUnchanged(donor.Path, donor.Archive);
        await BackupIndexedDocumentDedupAssertions.AssertArchiveUnchanged(selected.Path, selectedBytes);
    }

    private static async Task SourceAsync(EventingArtifactFixture fixture, long position,
        string[] source, VerifiedBackupCutSnapshot later)
    {
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(position);
        await Assert.That(EventingArtifactState.FullBytes(fixture.Source.Store))
            .IsEquivalentTo(source, CollectionOrdering.Matching);
        await VerifiedBackupMixedCutState.StateAsync(fixture, fixture.Source.Database, secondDocument: true);
        var original = global::KeyLoad.OrleansSerialization.Deserialize<StoredOutcome>(
            BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(fixture.Source.Store,
                fixture.Source.Partition, EventingArtifactFixture.Principal, fixture.Original.CommandId));
        await EventingArtifactState.SameResultAsync(original.Result,
            fixture.SubmitIssued(fixture.Source.Database, fixture.Original));
        await ReplaySecondAsync(fixture, later);
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(position);
        await Assert.That(EventingArtifactState.FullBytes(fixture.Source.Store))
            .IsEquivalentTo(source, CollectionOrdering.Matching);
    }

    private static async Task ReplaySecondAsync(EventingArtifactFixture fixture, VerifiedBackupCutSnapshot later)
    {
        if (later.SecondOperation is not { } operation || later.SecondResult is not { } result)
        {
            throw new InvalidOperationException(MissingSecondCut);
        }
        var stored = global::KeyLoad.OrleansSerialization.Deserialize<StoredOutcome>(
            BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(fixture.Source.Store,
                fixture.Source.Partition, EventingArtifactFixture.Principal, operation.CommandId));
        await EventingArtifactState.SameResultAsync(result, stored.Result);
        await EventingArtifactState.SameResultAsync(result,
            fixture.SubmitIssued(fixture.Source.Database, operation, explicitTime: false));
    }

    private const string MissingSecondCut = "The independently acknowledged second backup cut is required.";
}
