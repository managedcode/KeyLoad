using KeyLoad.Core.InternalSerialization;
using KeyLoad.Core.Search;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes SearchIdentity operations against the caller's database context.</summary>
public static class SearchIdentity
{
    internal static Command<DatabaseCommand> CreateVerifiedBm25CorpusPublication(this DatabaseEngine engine, string administratorId, Bm25CorpusPublicationPhaseCommand phase, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(work);
        var evaluatedAt = engine.EvaluationClock.GetUtcNow();
        return engine.Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            var administrator = engine.Principal(view, administratorId, evaluatedAt);
            if (!administrator.ClusterAdministrator)
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, Bm25CorpusProtocol.Administrator);
            }

            work.ChargeBytes(OrleansSerialization.Measure(phase));
            engine.RequireBm25Subject(view, administrator, phase, evaluatedAt);
            work.ChargeBytes(OrleansSerialization.Measure(phase));
            var payload = OrleansSerialization.Serialize(phase);
            return engine.CreatePreparedCommand(OperationKind.Bm25CorpusPublicationPhase, phase.CommandId, administrator.Id, evaluatedAt, payload, DatabaseCommandFingerprint.Compute(phase.CommandId, OperationKind.Bm25CorpusPublicationPhase, administrator.Id, CommandExecutionExecution.Identity<Bm25CorpusPublicationPhaseCommand>(payload)), work, view);
        });
    }

    internal static PrincipalRecord RequireBm25Subject(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord administrator, Bm25CorpusPublicationPhaseCommand phase, DateTimeOffset evaluatedAt)
    {
        if (!administrator.ClusterAdministrator)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, Bm25CorpusProtocol.Administrator);
        }

        Bm25CorpusShape.RequirePhase(phase);
        var current = phase.Current;
        var principal = engine.Principal(view, current.PrincipalId, evaluatedAt);
        engine.Authorization.Require(principal, current.Partition, current.Collection, Capability.Query | Capability.DocumentsRead);
        var resource = engine.Resource(view, current.Partition, current.Collection, ResourceKind.Collection);
        engine.Authorization.RequireFieldUse(principal, resource, current.Field);
        if (principal.PolicyEpoch != current.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, Bm25CorpusProtocol.Administrator);
        }

        return principal;
    }

    internal static void AuthorizeBm25CorpusPublication(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord administrator, Command<DatabaseCommand> operation)
    {
        var phase = OperationProtocol.Payload<Bm25CorpusPublicationPhaseCommand>(operation);
        OperationProtocol.RequireEnvelopeId(operation, phase.CommandId);
        engine.RequireBm25Subject(view, administrator, phase, operation.Timestamp);
    }
}
