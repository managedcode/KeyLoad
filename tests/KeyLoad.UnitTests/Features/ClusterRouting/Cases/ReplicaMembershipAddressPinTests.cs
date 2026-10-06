using System.Net;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ReplicaMembershipAddressPinTests
{
    private const string LocalSilo = "localhost:11111";
    private static readonly string[] Endpoints = [LocalSilo, LocalSilo, LocalSilo];
    private static readonly IPAddress MatchingAddress = IPAddress.Loopback;
    private static readonly IPAddress MismatchedAddress = IPAddress.Parse("192.0.2.41");

    [Test]
    public async Task SignedCallerAddressMustMatchActualNativeDnsBeforePinPublication()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15), TimeProvider.System);
        using var pins = new ReplicaMembershipAuthorityAddressPins(Endpoints, UnitRoutingOptions.Membership());
        var mismatch = await Assert.ThrowsAsync<KeyLoadException>(() =>
            pins.PinCallerAsync(0, MismatchedAddress, deadline.Token));
        await Assert.That(mismatch).IsNotNull();
        await Assert.That(mismatch!.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await pins.PinCallerAsync(0, MatchingAddress, deadline.Token);
        var changed = await Assert.ThrowsAsync<KeyLoadException>(() =>
            pins.PinCallerAsync(0, MismatchedAddress, deadline.Token));
        await Assert.That(changed).IsNotNull();
        await Assert.That(changed!.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await pins.PinCallerAsync(0, MatchingAddress, deadline.Token);
    }

    [Test]
    public async Task PreCancelledAdmissionDoesNotPinAndAnIndependentCallCanResolveNatively()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15), TimeProvider.System);
        using var pins = new ReplicaMembershipAuthorityAddressPins(Endpoints, UnitRoutingOptions.Membership());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            pins.PinCallerAsync(1, MatchingAddress, cancellation.Token));
        await pins.PinCallerAsync(1, MatchingAddress, deadline.Token);
    }

    [Test]
    public async Task ConcurrentNativeResolutionCallsShareOneStableVoterPin()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15), TimeProvider.System);
        using var pins = new ReplicaMembershipAuthorityAddressPins(Endpoints, UnitRoutingOptions.Membership());
        var calls = Enumerable.Range(0, 12)
            .Select(_ => pins.PinCallerAsync(2, MatchingAddress, deadline.Token)).ToArray();
        await Task.WhenAll(calls).ConfigureAwait(false);
        await pins.PinCallerAsync(2, MatchingAddress, deadline.Token);
    }
}
