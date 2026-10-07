using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class TwoRf3MembershipProfileTests
{
    [Test]
    public async Task AcMembership001To003UsesOneSixSiloMembershipAndKeepsBothDatabaseGroupsClosed()
    {
        using var deadlineTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(15), TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken, deadlineTimeout.Token);
        var failures = new List<Exception>();
        LocalRf3ImageTestSession? localImage = null;
        TwoRf3MembershipWave? wave = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            localImage = await LocalRf3ImageTestSession.StartIfSelectedAsync(deadline.Token).ConfigureAwait(false);
            var startedWave = await TwoRf3MembershipWave.StartAsync(localImage?.Selection, deadline.Token)
                .ConfigureAwait(false);
            wave = startedWave;
            await VerifyMembershipAsync(startedWave, deadline.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        var beforeWaveCleanup = failures.Count;
        if (wave is { } ownedWave)
        { await ServerFailureObserver.ObserveAsync(() => ownedWave.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        var waveCleanupSucceeded = wave is not null && failures.Count == beforeWaveCleanup;
        if (localImage is { } ownedImage)
        { await ServerFailureObserver.ObserveAsync(() => ownedImage.DisposeAsync(removeImage: waveCleanupSucceeded).AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task VerifyMembershipAsync(TwoRf3MembershipWave wave, CancellationToken cancellationToken)
    {
        await TwoRf3MembershipReadinessAssertions.VerifyAllNodesAsync(wave.Application, cancellationToken)
            .ConfigureAwait(false);
        await TwoRf3MembershipFingerprintOracle.VerifyAsync(wave.Application, wave.Profile, cancellationToken)
            .ConfigureAwait(false);
        await TwoRf3MembershipReadinessAssertions.VerifyPublicCallsClosedAsync(wave.Application,
            TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey, cancellationToken).ConfigureAwait(false);
        await TwoRf3MembershipReadinessAssertions.VerifyPublicCallsClosedAsync(wave.Application,
            TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey, cancellationToken).ConfigureAwait(false);
        await TwoRf3MembershipReadinessAssertions.VerifyAllNodesAsync(wave.Application, cancellationToken)
            .ConfigureAwait(false);
        await TwoRf3MembershipFingerprintOracle.VerifyAsync(wave.Application, wave.Profile, cancellationToken)
            .ConfigureAwait(false);
    }
}
