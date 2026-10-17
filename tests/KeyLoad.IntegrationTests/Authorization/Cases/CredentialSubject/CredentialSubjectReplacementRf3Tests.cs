using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class CredentialSubjectReplacementRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task SameCredentialUsesCurrentPersistedSubjectAcrossDiscoveryRebindRepairReplayAndTwoColdWaves()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = McpCallerDeadline.Create();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var state = await CredentialSubjectReplacementProvision.CreateAsync(fixture, deadline.Token);
                await CredentialSubjectReplacementClients.RunAsync(fixture, state, warm: true, deadline.Token);
                await CredentialSubjectReplacementCold.RunAsync(fixture, state, deadline.Token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
