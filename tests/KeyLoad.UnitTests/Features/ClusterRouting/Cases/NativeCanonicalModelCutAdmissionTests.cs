using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeCanonicalModelCutAdmissionTests
{
    [Test]
    public async Task GateBudgetRecoversAndOriginalStopJoinsOnlyAfterLeaseDisposal()
    {
        await NativeCanonicalModelCutAdmissionFixture.RunOwnedAsync(async fixture =>
        {
            using var first = fixture.Admission.Open(CancellationToken.None);
            var refused = Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Admission.Open(CancellationToken.None));
            await Assert.That(refused.Message).IsEqualTo(RequestCqrsProbeProtocol.InvalidFiles);
            first.Dispose();
            using var healthy = fixture.Admission.Open(CancellationToken.None);
            var join = fixture.Lifecycle.StopAdmissionAndJoin();
            await Assert.That(join.IsCompleted).IsFalse();
            Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Admission.Open(CancellationToken.None));
            healthy.Dispose();
            await join;
            healthy.Dispose();
            await fixture.Lifecycle.StopAdmissionAndJoin();
        });
    }

    [Test]
    public async Task RequestCancellationReleasesGateForHealthySubsequentAdmission()
    {
        await NativeCanonicalModelCutAdmissionFixture.RunOwnedAsync(async fixture =>
        {
            using var cancellation = new CancellationTokenSource();
            using var lease = fixture.Admission.Open(cancellation.Token);
            await cancellation.CancelAsync();
            var cancelled = Assert.ThrowsExactly<OperationCanceledException>(lease.Token.ThrowIfCancellationRequested);
            await Assert.That(cancelled.CancellationToken).IsEqualTo(lease.Token);
            lease.Dispose();
            Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Admission.Open(cancellation.Token));
            using var healthy = fixture.Admission.Open(CancellationToken.None);
            healthy.Token.ThrowIfCancellationRequested();
        });
    }

    [Test]
    public async Task ActualApplicationStoppingAndServerStoppingCancelAndDoNotLeakCallbacks()
    {
        await NativeCanonicalModelCutAdmissionFixture.RunOwnedAsync(async application =>
        {
            using var appLease = application.Admission.Open(CancellationToken.None);
            application.ApplicationLifetime.StopApplication();
            Assert.ThrowsExactly<OperationCanceledException>(appLease.Token.ThrowIfCancellationRequested);
            appLease.Dispose();
            Assert.ThrowsExactly<OperationCanceledException>(() => application.Admission.Open(CancellationToken.None));
            await application.Lifecycle.StopAdmissionAndJoin();
        });
        await NativeCanonicalModelCutAdmissionFixture.RunOwnedAsync(async server =>
        {
            using var serverLease = server.Admission.Open(CancellationToken.None);
            server.CancelStopping();
            await Assert.That(serverLease.Token.IsCancellationRequested).IsTrue();
            serverLease.Dispose();
            Assert.ThrowsExactly<OperationCanceledException>(() => server.Admission.Open(CancellationToken.None));
            await server.Lifecycle.StopAdmissionAndJoin();
        });
    }
}
