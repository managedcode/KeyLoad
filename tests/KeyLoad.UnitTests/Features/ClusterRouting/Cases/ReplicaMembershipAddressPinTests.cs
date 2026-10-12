namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ReplicaMembershipAddressPinTests
{
    [Test]
    public async Task NativeCallerAddressMatchesConfiguredVoterAndRejectsAnotherActualSilo()
        => await NativeMembershipAddressAdmissionFlow.RunAsync(
            NativeMembershipAddressAdmissionFlow.ResolveAsync, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public async Task PreCancelledAdmissionLeavesNativeMembershipUnchangedThenIndependentResolutionSucceeds()
        => await NativeMembershipAddressAdmissionFlow.RunAsync(
            NativeMembershipAddressAdmissionFlow.CancelledAsync, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public async Task TwelveConcurrentNativeResolutionCallsObserveOneCurrentFullAddress()
        => await NativeMembershipAddressAdmissionFlow.RunAsync(
            NativeMembershipAddressAdmissionFlow.ConcurrentAsync, TestContext.Current!.Execution.CancellationToken);
}
