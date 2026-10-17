using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal sealed class D2ImportCaptureSettlement(D2ImportSession session, PartitionMovementTransportRequest original)
{
    private readonly string identity = JsonData.Fingerprint(original.Envelope with { Nonce = Guid.Empty });

    internal Task<PartitionMovePhaseResult> SettleAsync(PartitionMovePeerEnvelope envelope, CancellationToken token)
    {
        if (!envelope.Body.Span.SequenceEqual(original.Envelope.Body.Span)
            || JsonData.Fingerprint(envelope with
            {
                Nonce = Guid.Empty
            }) != identity)
        {
            throw new InvalidOperationException("The actual admitted source capture changed.");
        }
        var principal = Principal(session, original);
        GrainRequestAuthority.RequireAdministrator(principal);
        var operation = session.Source.Database.CreateVerifiedPartitionMovementOperation(original.CommandId,
            principal.Id, session.Source.Database.EvaluationClock.GetUtcNow(), original.Envelope);
        return Task.FromResult(session.Source.Journal.Submit(operation, token).Get<PartitionMovePhaseResult>());
    }

    internal static PrincipalRecord Principal(D2ImportSession session, PartitionMovementTransportRequest request)
    {
        var id = PartitionMovementControlPrincipal.Resolve(session.Source.Database, request.Envelope);
        return session.Source.Store.Read(view => session.Source.Database.Principal(view, id,
            session.Source.Database.EvaluationClock.GetUtcNow()));
    }

    internal static async Task<PartitionMovementTransportRequest> VerifyAsync(D2ImportSession session,
        PartitionMovementTransportRequest request, CancellationToken token)
    {
        var failures = new List<Exception>();
        PartitionMovementTransportRequest? verified = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var mac = session.SourceAdmission.CreateControlMac();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var bytes = NativeSerialization.Serialize(request);
                verified = await session.SourceAdmission.VerifyAsync(bytes, mac.Sign(bytes, reply: false), token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return verified ?? throw new InvalidOperationException("No actual verified capture request returned.");
    }
}
