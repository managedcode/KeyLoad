using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.Server;
using P = KeyLoad.ClusterTests.Authorization.RowRevisionInferenceClusterProtocol;

namespace KeyLoad.ClusterTests.Authorization;

[NotInParallel]
internal sealed class RowRevisionInferenceClusterTests
{
    [Test]
    [Arguments(P.Put)]
    [Arguments(P.Patch)]
    [Arguments(P.Delete)]
    public async Task HiddenRowRevisionGuessesStayPrivateAcrossFreshSdkMcpAdmissionRepairAndTwoClusterColdCuts(string kind)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                await RowRevisionInferenceClusterFlow.RunAsync(fixture, kind, deadline.Token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
