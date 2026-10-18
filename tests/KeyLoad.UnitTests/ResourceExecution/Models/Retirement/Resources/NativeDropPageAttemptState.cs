namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record NativeDropPageAttemptState(NativeDropRuntimeProfile Profile, NativeDropPublicCall Call,
    ResourceRetirementDropParentLookupV1 Lookup, ResourceRetirementStateV1 OriginalState,
    NativeDropTableContext Context, ResourceRetirementAttemptContextV1 Scope, RegisteredPhysicalOwnerV1 Receiver)
{
    internal List<NativeDropExpectedOriginal> ExpectedOriginals { get; } = [];
    internal Guid? ReplacedAttempt
    {
        get; set;
    }
    internal ResourceRetirementPreparedOperationProof Proof { get; } = new(Profile.Source.Runtime.Core.DatabaseLimits);
    internal ResourceRetirementPreparedOperations Operations => new(Context.Phases, Proof);
}
