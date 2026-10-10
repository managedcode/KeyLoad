using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int EventVectorObservationVersion = 1;
    private const long EventVectorNoAppliedCommands = 0;
    private const string ChangedEventVectorObservation = "The original event vector source observation authority changed.";

    internal EventVectorSourceObservation ReadEventVectorSourceOutcome(string principalId,
        EventVectorSourcePhase originalPhase, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(originalPhase);
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        return Store.Read(view => ReadEventVectorSourceOutcome(view, principalId, originalPhase, work));
    }

    private EventVectorSourceObservation ReadEventVectorSourceOutcome(IKeyValueView view, string principalId,
        EventVectorSourcePhase originalPhase, ReadExecutionBudget work)
    {
        var bounded = work.CreateView(view);
        var principal = Principal(bounded, principalId, EvaluationClock.GetUtcNow());
        if (originalPhase.Source is null || originalPhase.SourceOwner is null)
        { throw Errors.Fail(ErrorCode.Validation, ChangedEventVectorObservation); }
        Authorization.Require(principal, originalPhase.Source.Partition, originalPhase.Source.Resource,
            Capability.SubscriptionsManage);
        Authorization.Require(principal, originalPhase.Source.Partition, originalPhase.Source.Resource,
            SourceReadCapability(originalPhase.Source));
        var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
        var mutation = EventVectorSourceMutationValidation.ReadOriginal(originalPhase, admission);
        var placement = StreamTraversalPlacement(bounded, mutation.Source.Partition, mutation.SourceOwner);
        if (principal.Id != mutation.PrincipalId || principal.PolicyEpoch != mutation.SourcePolicyEpoch
            || placement.Revision != mutation.LogicalPlacementRevision
            || placement.DirectoryRevision != mutation.DirectoryFence)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, ChangedEventVectorObservation); }
        var resource = SourceResource(bounded, mutation.Source);
        if (resource.SchemaVersion != mutation.SourceSchemaVersion)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, ChangedEventVectorObservation); }
        if (mutation.Role != EventVectorSourcePhaseRole.Release)
        { _ = EventVectorStoredSourceHead(bounded, mutation.Source); }
        var outcome = EventVectorSourceOutcomeBytes.Read(bounded, originalPhase, admission);
        var applied = EventVectorNoAppliedCommands;
        bounded.ReadValue(KeySpace.AppliedBytes, bytes => applied = NativeSerialization.Deserialize<long>(bytes));
        var identity = Store.Identity;
        var result = new EventVectorSourceObservation(EventVectorObservationVersion, originalPhase.PhaseCommandId,
            originalPhase.OriginalBodyDigest.ToArray(), originalPhase.Source, originalPhase.SourceOwner,
            identity.NodeId, identity.ReadGeneration, Store.Position, applied, principal.Id, principal.PolicyEpoch,
            placement, outcome)
        { CurrentPinRowBytes = EventVectorCurrentPinBytes.Read(bounded, originalPhase, mutation, admission) };
        work.CheckResult(result);
        return result;
    }
}
