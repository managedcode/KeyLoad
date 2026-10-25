namespace KeyLoad.FunctionalTests.Messaging;

internal sealed class RemoteTransferRepairArithmeticWholeFlowTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task InvalidPersistedRepairGenerationSumRefusesOriginalCompletionThenExactRowRepairTwoColdAndHealthyAck(bool advancedHistory)
        => RemoteTransferRepairArithmeticFlow.RunAsync(advancedHistory);

    [Test]
    public Task ExchangedPersistedRepairGenerationsRefuseOriginalCompletionThenExactRowRepairTwoColdAndHealthyAck()
        => RemoteTransferRepairArithmeticFlow.RunAsync(advancedHistory: true, exchangedGenerations: true);
}
