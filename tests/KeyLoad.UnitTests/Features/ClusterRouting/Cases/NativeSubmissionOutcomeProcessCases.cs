using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeSubmissionOutcomeProcessCases
{
    [Test]
    public Task GenuineStoredBatchReturnsCompleteOriginalNativeOutcomeWithoutEffects()
        => C1OutcomeInspectionFixture.RunOwnedAsync(async fixture =>
        {
            var seed = await NativeSubmissionOutcomeProcessSeed.CreateAsync(fixture).ConfigureAwait(false);
            await NativeSubmissionOutcomeProcessAssertions.RequireHealthyAsync(fixture, seed).ConfigureAwait(false);
            await NativeSubmissionOutcomeProcessAssertions.RequireHealthyAsync(fixture, seed).ConfigureAwait(false);
        });

    [Test]
    public Task GenuineOwnerAndMissingCommandRefusalsKeepAllRowsAndFollowingFullInspectionHealthy()
        => C1OutcomeInspectionFixture.RunOwnedAsync(async fixture =>
        {
            var seed = await NativeSubmissionOutcomeProcessSeed.CreateAsync(fixture).ConfigureAwait(false);
            var wrongNode = seed.Request with { ExpectedNodeId = Guid.NewGuid() };
            var wrongIncarnation = seed.Request with { Incarnation = Guid.NewGuid() };
            var missing = seed.Request with { CommandId = Guid.NewGuid() };
            foreach (var request in new[] { wrongNode, wrongIncarnation, missing })
            {
                var phase = request.CommandId == seed.Command.CommandId
                    ? C1OutcomeInspectionFailurePhase.OpenStore : C1OutcomeInspectionFailurePhase.ReadOutcome;
                await NativeSubmissionOutcomeProcessAssertions.RequireRejectedAsync(fixture, request, phase)
                    .ConfigureAwait(false);
                await NativeSubmissionOutcomeProcessAssertions.RequireHealthyAsync(fixture, seed).ConfigureAwait(false);
            }
        });

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task GenuineMalformedOrOverboundNativeRowRefusesWithoutEffectsAndOriginalBytesRecover(bool overbound)
        => C1OutcomeInspectionFixture.RunOwnedAsync(async fixture =>
        {
            var seed = await NativeSubmissionOutcomeProcessSeed.CreateAsync(fixture).ConfigureAwait(false);
            var invalid = overbound
                ? new byte[C1SubmissionOutcomeInspectionProtocol.MaximumNativeRecordBytes + 1] : new byte[] { 0xFF };
            NativeSubmissionOutcomeProcessCut.ReplaceOwnedRow(fixture, seed.Key, invalid);
            await NativeSubmissionOutcomeProcessAssertions.RequireRejectedAsync(fixture, seed.Request,
                C1OutcomeInspectionFailurePhase.ReadOutcome).ConfigureAwait(false);
            NativeSubmissionOutcomeProcessCut.ReplaceOwnedRow(fixture, seed.Key, seed.OriginalBytes);
            await NativeSubmissionOutcomeProcessAssertions.RequireHealthyAsync(fixture, seed).ConfigureAwait(false);
        });
}
