using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Retires actual source families through bounded native cleanup, original publication grant and A ACKs.</summary>
internal static class ControlledPartitionMovementRetireFlow
{
    private const int Version = 1;
    private const int ControlOrdinal = 0;
    private const int FirstFamily = 0;
    private const int FamilyStep = 1;

    internal static async Task<(PartitionMovePhaseResult Terminal, Guid GrantId,
        ReadOnlyMemory<byte> OriginalBody)> ExecuteAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovePhaseResult finalized,
        string callerAddress, DateTimeOffset wholeExpiresAt, TimeSpan issuedExpiryWindow, CancellationToken cancellationToken)
    {
        var publication = await ControlledPartitionMovementPublicationRead.ReadAsync(source, corpus, finalized);
        var familyCount = PartitionMoveCleanupFamilies.All.Length;
        for (var family = FirstFamily; family <= familyCount; family += FamilyStep)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (request, issued, outcome, expiry) = await ApplyFamilyAsync(source, runtime, admission,
                corpus, finalized, publication, family, familyCount, callerAddress, wholeExpiresAt,
                issuedExpiryWindow, cancellationToken);
            var actual = outcome.Get<PartitionMovePhaseResult>();
            var cleanup = actual.Cleanup ?? throw new InvalidOperationException("Actual retirement progress is absent.");
            var acknowledgmentExpiry = expiry;
            if (family == FirstFamily && issuedExpiryWindow > TimeSpan.Zero)
            {
                await ControlledPartitionMovementNaturalExpiryAssertions.AssertAsync(source, target,
                    runtime, admission, request, issued, outcome, cancellationToken);
                acknowledgmentExpiry = ControlledPartitionMovementFirstPhaseExpiry.Create(source, runtime,
                    wholeExpiresAt, cancellationToken);
            }
            await AcknowledgeAsync(source, runtime, admission, corpus, finalized, actual, family,
                callerAddress, acknowledgmentExpiry, cancellationToken);
            if (family == familyCount)
            {
                await Assert.That(JsonDefaults.Serialize(cleanup.Completion)
                    .SequenceEqual(JsonDefaults.Serialize(actual.Journal))).IsTrue();
                return (actual, ControlledPartitionMovementRetirePhaseIds.Grant(family), request.Envelope.Body);
            }
            await Assert.That(cleanup.Completion).IsNull();
            await Assert.That(cleanup.NextFamily).IsEqualTo(checked(family + FamilyStep));
            await Assert.That(cleanup.NextBatch).IsEqualTo(checked(family + FamilyStep));
        }
        throw new InvalidOperationException("The original terminal source retirement journal is absent.");
    }

    private static async Task<(PartitionMovementTransportRequest Request, PartitionMovePhaseGrant Issued,
        OperationResult Outcome, DateTimeOffset Expiry)> ApplyFamilyAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovePhaseResult finalized,
        PartitionMovePublishedPlacement publication, int family, int familyCount, string callerAddress,
        DateTimeOffset wholeExpiresAt, TimeSpan issuedExpiryWindow, CancellationToken cancellationToken)
    {
        var expiry = ControlledPartitionMovementFirstPhaseExpiry.Create(source, runtime, wholeExpiresAt, cancellationToken);
        if (family == FirstFamily && issuedExpiryWindow > TimeSpan.Zero)
        {
            var deliberate = source.Database.EvaluationClock.GetUtcNow() + issuedExpiryWindow;
            expiry = deliberate < expiry ? deliberate : expiry;
        }
        var authorization = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime,
            admission, ControlledPartitionMovementRetireRequest.Authorize(finalized, publication,
                family, family, corpus, callerAddress, expiry), cancellationToken);
        await Assert.That(authorization.Error).IsNull();
        var request = ControlledPartitionMovementRetireRequest.Retire(finalized, publication,
            family, family, authorization.Get<PartitionMovePhaseResult>(), corpus, callerAddress, expiry);
        var issued = authorization.Get<PartitionMovePhaseResult>().Grant
            ?? throw new InvalidOperationException("The actual first source retirement grant is absent.");
        await Assert.That(request.Envelope.ExpiresAt).IsEqualTo(issued.ExpiresAt);
        if (family == FirstFamily)
        { await ControlledPartitionMovementRetireExpiryAssertions.AssertAsync(source, runtime, admission, request, cancellationToken); }
        var outcome = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime,
            admission, request, cancellationToken);
        await Assert.That(outcome.Error).IsNull();
        var actual = outcome.Get<PartitionMovePhaseResult>();
        var cleanup = actual.Cleanup ?? throw new InvalidOperationException("Actual retirement progress is absent.");
        await ControlledPartitionMovementRetireStateAssertions.AssertAsync(cleanup, actual.Journal,
            finalized.Journal.ControlIntentDigest, family, familyCount);
        return (request, issued, outcome, expiry);
    }

    private static async Task AcknowledgeAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovePhaseResult captured,
        PartitionMovePhaseResult staged, int batchOrdinal, string callerAddress, DateTimeOffset expiry,
        CancellationToken cancellationToken)
    {
        var control = captured.Control ?? throw new InvalidOperationException("Actual Captured control is absent.");
        var body = NativeSerialization.Serialize(new PartitionMoveAcknowledgeBody(
            ControlledPartitionMovementRetirePhaseIds.Grant(batchOrdinal), staged.Journal));
        var envelope = new PartitionMovePeerEnvelope(Version, control.MoveId, control.Partition,
            corpus.Control.Owner, control.SourcePlacement, corpus.Destination.Owner,
            captured.Journal.ControlIntentDigest, PartitionMovePeerStage.ControlAcknowledge,
            ControlOrdinal, expiry, Guid.NewGuid(), body);
        var result = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            new(ControlledPartitionMovementRetirePhaseIds.Acknowledgement(batchOrdinal), envelope, null,
                corpus.Control.Owner.VoterIds.First(), callerAddress, PartitionMovementTransportAction.Apply,
                Guid.Empty, ControlOrdinal), cancellationToken);
        await Assert.That(result.Error).IsNull();
        var grant = result.Get<PartitionMovePhaseResult>().Grant
            ?? throw new InvalidOperationException("Actual source-retirement settlement is absent.");
        await Assert.That(grant.GrantId).IsEqualTo(ControlledPartitionMovementRetirePhaseIds.Grant(batchOrdinal));
        await Assert.That(JsonDefaults.Serialize(grant.Settlement)
            .SequenceEqual(JsonDefaults.Serialize(staged.Journal))).IsTrue();
    }
}
