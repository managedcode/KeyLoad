namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class TwoRf3MembershipProfileTests
{
    [Test]
    public async Task AcMembership001To003UsesOneSixSiloMembershipAndKeepsBothDatabaseGroupsClosed()
    {
        using var deadlineTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(15), TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken, deadlineTimeout.Token);
        await using var wave = await TwoRf3MembershipWave.StartAsync(deadline.Token).ConfigureAwait(false);
        await TwoRf3MembershipReadinessAssertions.VerifyAllNodesAsync(wave.Application, deadline.Token).ConfigureAwait(false);
        await TwoRf3MembershipFingerprintOracle.VerifyAsync(wave.Application, wave.Profile, deadline.Token)
            .ConfigureAwait(false);
        await TwoRf3MembershipReadinessAssertions.VerifyPublicCallsClosedAsync(wave.Application,
            TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey, deadline.Token).ConfigureAwait(false);
        await TwoRf3MembershipReadinessAssertions.VerifyPublicCallsClosedAsync(wave.Application,
            TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey, deadline.Token).ConfigureAwait(false);
    }
}
