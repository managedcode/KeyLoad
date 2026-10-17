namespace KeyLoad.UnitTests.Features.ClusterRouting;

[RequestCqrsDataSource]
internal sealed class RequestCqrsMovementAdmissionTests(RequestCqrsClusterFixture fixture)
{
    [Test]
    [Arguments(PartitionMovePhase.Prepared)]
    [Arguments(PartitionMovePhase.Fenced)]
    [Arguments(PartitionMovePhase.Captured)]
    [Arguments(PartitionMovePhase.Transferring)]
    [Arguments(PartitionMovePhase.Installed)]
    [Arguments(PartitionMovePhase.Published)]
    [Arguments(PartitionMovePhase.Aborting)]
    public Task SignedMovementAdmitsPersistedSuffixRepeatsAndOneTerminal(PartitionMovePhase phase)
        => PartitionMovementStreamAdmissionFlow.SuffixAsync(fixture, phase);

    [Test]
    [Arguments(PartitionMovementStreamFault.Unbound)]
    [Arguments(PartitionMovementStreamFault.OtherOperation)]
    [Arguments(PartitionMovementStreamFault.WrongRequest)]
    [Arguments(PartitionMovementStreamFault.NonePhase)]
    [Arguments(PartitionMovementStreamFault.RetiredPhase)]
    [Arguments(PartitionMovementStreamFault.AbortedPhase)]
    [Arguments(PartitionMovementStreamFault.UnknownPhase)]
    [Arguments(PartitionMovementStreamFault.MixedPhase)]
    [Arguments(PartitionMovementStreamFault.WrongSequence)]
    [Arguments(PartitionMovementStreamFault.WrongMessage)]
    [Arguments(PartitionMovementStreamFault.EventId)]
    [Arguments(PartitionMovementStreamFault.FinalAttached)]
    [Arguments(PartitionMovementStreamFault.WrongEventType)]
    public Task MovementProgressRefusesUnboundOtherAndMalformedFrames(PartitionMovementStreamFault fault)
        => PartitionMovementStreamAdmissionFlow.RefusedAsync(fixture, fault);

    [Test]
    public Task MovementKeepsFiniteFrameQuotaAndZeroProgressTerminalReplay()
        => PartitionMovementStreamAdmissionFlow.QuotaAndReplayAsync(fixture);
}
