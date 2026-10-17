using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using P = KeyLoad.IntegrationTests.Features.Authorization.RowRevisionInferenceRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

[NotInParallel]
internal sealed class RowRevisionInferenceRf3Tests
{
    [Test]
    [Arguments(P.Put)]
    [Arguments(P.Patch)]
    [Arguments(P.Delete)]
    public async Task HiddenRowRevisionGuessesStayPrivateAcrossFreshSdkMcpAdmissionRepairAndTwoRf3ColdCuts(string kind)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                await RowRevisionInferenceRf3Flow.RunAsync(fixture, kind, deadline.Token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
