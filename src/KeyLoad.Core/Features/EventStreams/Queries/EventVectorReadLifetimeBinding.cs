using System.Collections.Immutable;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

/// <summary>Observes native-authorized EventVector dependencies inside the original charged read cut.</summary>
internal sealed class EventVectorReadLifetimeBinding(DatabaseEngine database, IKeyValueView view, ReadExecutionBudget work)
{
    internal void Scope(EventFeedControlRequest request)
        => _ = database.BindAuthorizedReadResourceMetadata(view, request.ControlPartition.TenantId,
            request.ControlPartition.DatabaseId, request.Scope.Domain, request.Scope.Resource,
            request.Scope.Kind == EventSourceKind.Topic ? ResourceKind.Topic : ResourceKind.StreamSet, work);

    internal void Authorize(IAuthorizationPolicy authorization, PrincipalRecord principal,
        ImmutableArray<EventVectorAuthorizationScope> scopes, EventVectorAdmissionPolicy admission)
    {
        EventVectorParentScopes.Authorize(authorization, principal, scopes, admission);
        foreach (var scope in scopes)
        {
            work.Check();
            _ = database.BindAuthorizedReadResourceMetadata(view, scope.Partition.TenantId,
                scope.Partition.DatabaseId, scope.Partition.TransactionDomainId, scope.Resource,
                scope.Kind == EventSourceKind.Topic ? ResourceKind.Topic : ResourceKind.StreamSet, work);
        }
    }

    internal void Witness(EventVectorCoverageWitness witness, EventVectorAdmissionPolicy admission)
    {
        var lifetime = EventVectorCoverageLifetimeValidation.Require(witness, admission);
        ResourceRetirementStorage.RequireOpen(view, lifetime, database.Limits);
        work.ObserveAuthenticatedResourceLifetimes([lifetime]);
    }

    internal void Phase(ReadOnlyMemory<byte> bytes, EventVectorAdmissionPolicy admission)
    {
        if (bytes.IsEmpty)
        {
            return;
        }
        admission.RequireEncodedBytes(bytes.Length);
        var phase = NativeSerialization.Deserialize<EventVectorControlPhase>(bytes.Span);
        EventVectorControlFields.Require(phase, admission);
        Witness(EventVectorCoverageWitnessEncoding.Read(phase.OriginalCoverageWitness,
            phase.OriginalRequest, admission), admission);
    }

    internal void Capture(EventVectorCoverageCapture capture)
    {
        if (capture.SourceDefinition is null && capture.SourceLifetime is null)
        {
            return;
        }
        var definition = capture.SourceDefinition ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        var lifetime = capture.SourceLifetime ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        ResourceLifetimeIdentity.Validate(lifetime, capture.ControlPartition.TenantId,
            capture.ControlPartition.DatabaseId, definition);
        ResourceRetirementStorage.RequireOpen(view, lifetime, database.Limits);
        work.ObserveAuthenticatedResourceLifetimes([lifetime]);
    }

    private const string Invalid = "The event vector read lifetime proof is unavailable.";
}
