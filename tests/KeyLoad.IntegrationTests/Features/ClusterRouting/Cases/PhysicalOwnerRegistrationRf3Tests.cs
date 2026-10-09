using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class PhysicalOwnerRegistrationRf3Tests
{
    private const int NoFailuresCount = 0;

    [Test]
    public async Task AcOwnerRegister001To003RegistersConfiguredOwnersThenJoinsAllNodesAndReopensLiteralDirectory()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15), TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken,
            timeout.Token);
        var failures = new List<Exception>();
        var evidence = new PhysicalOwnerRegistrationRf3Evidence();
        TwoRf3MembershipWave? wave = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            evidence.Enter(PhysicalOwnerRegistrationRf3Phase.WaveStart);
            wave = await TwoRf3MembershipWave.StartRegistrationAsync(deadline.Token).ConfigureAwait(false);
            await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, evidence, deadline.Token).ConfigureAwait(false);
            evidence.Enter(PhysicalOwnerRegistrationRf3Phase.Fingerprint);
            await TwoRf3MembershipFingerprintOracle.VerifyAsync(wave.Application, wave.Profile, deadline.Token).ConfigureAwait(false);
            evidence.Enter(PhysicalOwnerRegistrationRf3Phase.DirectoryRead);
            var expected = await PhysicalOwnerDirectoryRf3Assertions.ExpectedAsync(wave.Application, wave.Profile,
                deadline.Token).ConfigureAwait(false);
            evidence.Enter(PhysicalOwnerRegistrationRf3Phase.SourcePublicClosed);
            await TwoRf3MembershipReadinessAssertions.VerifyPublicCallsClosedAsync(wave.Application,
                TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey, deadline.Token).ConfigureAwait(false);
            evidence.Enter(PhysicalOwnerRegistrationRf3Phase.DestinationPublicClosed);
            await TwoRf3MembershipReadinessAssertions.VerifyPublicCallsClosedAsync(wave.Application,
                TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey, deadline.Token).ConfigureAwait(false);
            evidence.Enter(PhysicalOwnerRegistrationRf3Phase.StopForReopen);
            var root = await wave.StopForDirectoryReadAsync().ConfigureAwait(false);
            evidence.Enter(PhysicalOwnerRegistrationRf3Phase.Reopen);
            await PhysicalOwnerDirectoryRf3Assertions.VerifyReopenedAsync(root, expected).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (failures.Count > NoFailuresCount)
        {
            ServerFailureObserver.Observe(() => evidence.Write(wave?.application, wave is not null, wave?.applicationDisposed ?? false, failures, timeout.Token, deadline.Token,
            TestContext.Current!.Execution.CancellationToken), failures);
        }
        if (wave is { } owned)
        { await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
