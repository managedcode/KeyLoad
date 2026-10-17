namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class EventMessageSensitiveReplayTests
{
    [Test, Arguments(false, false, false), Arguments(false, false, true),
     Arguments(true, false, false), Arguments(true, true, true)]
    public Task CurrentResourceBodyAndHeaderPolicyRefusesOriginalDeliveryThenRepairedNewWorkAndColdPreserveExactAuthority(
        bool subscription, bool dataAuthority, bool header)
        => EventMessageSensitiveReplayTrial.RunAsync(subscription, dataAuthority, header, TestContext.Current!.Execution.CancellationToken);
    [Test]
    public Task AbsentProtectedPathPreservesCompleteUnicodeArrayReceiptAfterCurrentPolicyChangeAndColdThenDeniedProducerAndFreshHealthyWork()
        => EventMessageSensitiveSafeReplayTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}

