using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Features.Messaging;

[NotInParallel]
internal sealed class CompetingInboxPublicTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task SeparatelyIssuedSdkAndOfficialMcpCompeteForOneInboxThenCurrentInputRepairAndTwoCold(bool equivalent)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var joined = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, TestContext.Current!.Execution.CancellationToken);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                fixture.RegisterNativeCoverageCase<CompetingInboxPublicTests>(nameof(SeparatelyIssuedSdkAndOfficialMcpCompeteForOneInboxThenCurrentInputRepairAndTwoCold));
                await CompetingInboxPublicFlow.RunAsync(new(fixture, equivalent, TimeProvider.System), joined.Token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
