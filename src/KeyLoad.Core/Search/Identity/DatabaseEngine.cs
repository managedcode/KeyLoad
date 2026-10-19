using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal Command<DatabaseCommand> CreateVerifiedBm25CorpusPublication(string administratorId,
        Bm25CorpusPublicationPhaseCommand phase, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(work);
        var evaluatedAt = EvaluationClock.GetUtcNow();
        return Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            var administrator = Principal(view, administratorId, evaluatedAt);
            if (!administrator.ClusterAdministrator)
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, Bm25CorpusProtocol.Administrator);
            }
            work.ChargeBytes(NativeSerialization.Measure(phase));
            RequireBm25Subject(view, administrator, phase, evaluatedAt);
            work.ChargeBytes(NativeSerialization.Measure(phase));
            var payload = NativeSerialization.Serialize(phase);
            RequireNativeBudget(payload.Length);
            return CreatePreparedCommand(OperationKind.Bm25CorpusPublicationPhase, phase.CommandId,
                administrator.Id, evaluatedAt, payload, DatabaseCommandFingerprint.Compute(phase.CommandId,
                    OperationKind.Bm25CorpusPublicationPhase, administrator.Id, Identity<Bm25CorpusPublicationPhaseCommand>(payload)), work, view);
        });
    }

    private PrincipalRecord RequireBm25Subject(IKeyValueView view, PrincipalRecord administrator,
        Bm25CorpusPublicationPhaseCommand phase, DateTimeOffset evaluatedAt)
    {
        if (!administrator.ClusterAdministrator)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, Bm25CorpusProtocol.Administrator);
        }
        Bm25CorpusShape.RequirePhase(phase, Limits);
        var current = phase.Current;
        var principal = Principal(view, current.PrincipalId, evaluatedAt);
        Authorization.Require(principal, current.Partition, current.Collection,
            Capability.Query | Capability.DocumentsRead);
        var resource = Resource(view, current.Partition, current.Collection, ResourceKind.Collection);
        Authorization.RequireFieldUse(principal, resource, current.Field);
        if (principal.PolicyEpoch != current.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, Bm25CorpusProtocol.Administrator);
        }
        return principal;
    }

    private void AuthorizeBm25CorpusPublication(IKeyValueView view, PrincipalRecord administrator,
        Command<DatabaseCommand> operation)
    {
        var phase = Payload<Bm25CorpusPublicationPhaseCommand>(operation);
        RequireEnvelopeId(operation, phase.CommandId);
        RequireBm25Subject(view, administrator, phase, operation.Timestamp);
    }
}
