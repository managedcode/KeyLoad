namespace KeyLoad.UnitTests.Features.OrleansExecution;

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
    public Task AcCrsWork001RejectedAdmissionStillSettlesTheRealActivation()
        => RequestWorkKernelCases.AssertRejectedAdmissionSettlesAsync(fixture);
}
