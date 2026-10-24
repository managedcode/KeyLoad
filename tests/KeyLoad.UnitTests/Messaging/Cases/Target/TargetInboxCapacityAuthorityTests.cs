namespace KeyLoad.UnitTests.Messaging;

internal sealed class TargetInboxCapacityAuthorityTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public Task RetainedCapacityCorruptionRefusesWholeInboxBeforeAndAfterColdThenExactRepairResumesOriginalIdsAndProtectedEffects(int fault)
        => TargetInboxCapacityAuthorityFlow.RunAsync(fault);
}
