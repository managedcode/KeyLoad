using KeyLoad.CrashHost.Features.ClusterReplication;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeSubmissionJournalProcessCases
{
    [Test]
    public Task GenuineStoppedNativeJournalAndCompleteCanonicalOutcomeReturnWithoutEffects()
        => NativeSubmissionJournalProcessFixture.RunOwnedAsync(async fixture =>
        {
            await NativeSubmissionJournalProcessAssertions.RequireHealthyAsync(fixture).ConfigureAwait(false);
            await NativeSubmissionJournalProcessAssertions.RequireHealthyAsync(fixture).ConfigureAwait(false);
        });

    [Test]
    public Task GenuineIdentityMembershipAndEntryRefusalsKeepBothStoresAndFollowingInspectionHealthy()
        => NativeSubmissionJournalProcessFixture.RunOwnedAsync(async fixture =>
        {
            var original = fixture.Request;
            var requests = new[]
            {
                original with { ExpectedCanonicalNodeId = Guid.NewGuid() },
                original with { Incarnation = Guid.NewGuid() },
                original with { Voters = [original.LocalVoter, "journal-foreign-b", "journal-foreign-c"] },
                original with { EntryTerm = original.EntryTerm + 1 },
                original with { EntryIndex = original.EntryIndex + 1 }
            };
            foreach (var request in requests)
            {
                var phase = request.ExpectedCanonicalNodeId != original.ExpectedCanonicalNodeId
                    || request.Incarnation != original.Incarnation
                    ? C1OutcomeInspectionFailurePhase.OpenStore : C1OutcomeInspectionFailurePhase.ReadOutcome;
                await NativeSubmissionJournalProcessAssertions.RequireRejectedAsync(fixture, request, phase)
                    .ConfigureAwait(false);
                await NativeSubmissionJournalProcessAssertions.RequireHealthyAsync(fixture).ConfigureAwait(false);
            }
        });

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task GenuineMalformedOrOverboundEntryRefusesAndOriginalNativeBytesRecover(bool overbound)
        => NativeSubmissionJournalProcessFixture.RunOwnedAsync(async fixture =>
        {
            var invalid = overbound
                ? new byte[C1SubmissionJournalInspectionProtocol.MaximumNativeRecordBytes + 1] : new byte[] { 0xFF };
            NativeSubmissionJournalProcessCut.ReplaceEntry(fixture, invalid);
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => NativeSubmissionJournalProcessAssertions.RequireRejectedAsync(
                fixture, fixture.Request, C1OutcomeInspectionFailurePhase.ReadOutcome), failures).ConfigureAwait(false);
            ServerFailureObserver.Observe(() => NativeSubmissionJournalProcessCut.ReplaceEntry(fixture,
                fixture.Seed.NativeEntry), failures);
            await ServerFailureObserver.ObserveAsync(() => NativeSubmissionJournalProcessAssertions.RequireHealthyAsync(fixture),
                failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
        });
}
