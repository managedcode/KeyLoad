using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Authorization;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey), NotInParallel]
internal sealed class EventMessageSensitivePublicRf3Tests(ClusterFixture fixture)
{
    [Test, Arguments(false, false, false), Arguments(false, false, true),
     Arguments(true, false, false), Arguments(true, true, true)]
    public Task CurrentBodyHeaderPoliciesRevokeRepairOriginalRefusalFreshDeliveryDlqAndSameOwnerColdFourRoutes(
        bool subscription, bool dataAuthority, bool header)
        => EventMessageSensitivePublicTrial.RunAsync(fixture, subscription, dataAuthority, header);
}
