namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class MovementRetireJournalCaptureTests
{
    [Test]
    public Task ActualNativeRetireNullControlRoundTripsScopedJournalAndRejectsCorruptShapes()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(async (source, target, listeners, corpus) =>
        {
            await ControlledPartitionMovementTerminalTrial.ExecuteAsync(source, target, listeners, corpus);
            await MovementRetireJournalCaptureTrial.RequireAsync(source);
        });
}
