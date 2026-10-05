namespace KeyLoad.UnitTests.Features.ClusterRouting;

[RequestCqrsDataSource]
internal sealed class NativeRequestWorkKernelTests(RequestCqrsClusterFixture fixture)
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
