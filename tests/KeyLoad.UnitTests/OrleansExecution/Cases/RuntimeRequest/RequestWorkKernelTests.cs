namespace KeyLoad.UnitTests.OrleansExecution;

[RequestCqrsHostedDataSource]
internal sealed class RequestWorkKernelTests(RequestCqrsHostedRuntimeFixture fixture)
{
    [Test]
    public Task AcCrsWork001LazyStreamDoesNotAcquireProducerWork()
        => RequestWorkKernelCases.AssertLazyStreamAsync(fixture);

    [Test]
    public Task AcCrsWork001PausedConsumerAndHeldProducerKeepDrainOpen()
        => RequestWorkKernelCases.AssertHeldProducerJoinAsync(fixture);

    [Test]
    public Task AcCrsWork001CallbackFailureIsObservedAfterProducerAndActivationSettle()
        => RequestWorkKernelCases.AssertCallbackFailureJoinAsync(fixture);

    [Test]
    public Task AcCrsWork001DuplicateIdentityStillSettlesTheRealActivation()
        => RequestWorkKernelCases.AssertDuplicateIdentitySettlesAsync(fixture);
}
