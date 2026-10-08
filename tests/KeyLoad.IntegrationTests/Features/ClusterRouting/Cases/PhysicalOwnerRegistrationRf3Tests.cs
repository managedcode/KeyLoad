using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class PhysicalOwnerRegistrationRf3Tests
{
    [Test]
    public async Task AcOwnerRegister001To003RegistersConfiguredOwnersThenJoinsAllNodesAndReopensLiteralDirectory()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15), TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken,
            timeout.Token);
        var failures = new List<Exception>();
        TwoRf3MembershipWave? wave = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartRegistrationAsync(deadline.Token).ConfigureAwait(false);
            await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, deadline.Token).ConfigureAwait(false);
            await TwoRf3MembershipFingerprintOracle.VerifyAsync(wave.Application, wave.Profile, deadline.Token).ConfigureAwait(false);
            var expected = await PhysicalOwnerDirectoryRf3Assertions.ExpectedAsync(wave.Application, wave.Profile,
                deadline.Token).ConfigureAwait(false);
            await TwoRf3MembershipReadinessAssertions.VerifyPublicCallsClosedAsync(wave.Application,
                TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey, deadline.Token).ConfigureAwait(false);
            await TwoRf3MembershipReadinessAssertions.VerifyPublicCallsClosedAsync(wave.Application,
                TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey, deadline.Token).ConfigureAwait(false);
            var root = await wave.StopForDirectoryReadAsync().ConfigureAwait(false);
            await PhysicalOwnerDirectoryRf3Assertions.VerifyReopenedAsync(root, expected).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (wave is { } owned)
        { await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
