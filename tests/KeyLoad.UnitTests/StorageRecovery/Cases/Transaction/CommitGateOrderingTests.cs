namespace KeyLoad.UnitTests.Features.StorageRecovery;

/// <summary>AC-STORAGE-GATE-001: readers never wait for a writer's compilation or journal flush, only for its apply.</summary>
internal sealed class CommitGateOrderingTests
{
    [Test]
    public Task ReadersProceedWhileAWriterCompilesAndFlushesThenWaitOnlyForApply()
        => PreparationOwnershipOwner.RunAsync(CommitGateOrderingFlow.RunAsync);
}
