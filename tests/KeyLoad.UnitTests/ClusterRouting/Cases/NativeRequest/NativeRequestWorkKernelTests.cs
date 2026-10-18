namespace KeyLoad.UnitTests.Features.ClusterRouting;

[RequestCqrsHostedDataSource]
internal sealed class NativeRequestWorkKernelTests(RequestCqrsHostedRuntimeFixture fixture)
{
    [Test]
    public Task AcCrsWork001LazyStreamDoesNotAcquireProducerWork()
        => NativeRequestWorkKernelCases.AssertLazyStreamAsync(fixture);

    [Test]
    public Task AcCrsWork001PausedConsumerAndHeldProducerKeepDrainOpen()
        => NativeRequestWorkKernelCases.AssertHeldProducerJoinAsync(fixture);

    [Test]
    public Task AcCrsWork001CallbackFailureIsObservedAfterProducerAndActivationSettle()
        => NativeRequestWorkKernelCases.AssertCallbackFailureJoinAsync(fixture);

    [Test]
    public Task AcCrsWork001RejectedAdmissionStillSettlesTheRealActivation()
        => NativeRequestWorkKernelCases.AssertRejectedAdmissionSettlesAsync(fixture);
}
