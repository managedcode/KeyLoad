using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeAnnPublicColdReader
{
    private const int Version = 1;
    private const string Tool = "keyload_search_ann_read";
    private const Capability Healthy = Capability.Query | Capability.DocumentsRead | Capability.VectorSearch;
    private const Capability Denied = Capability.Query | Capability.DocumentsRead;

    internal static async Task<NativeAnnPublicColdSeed> RunAsync(ClusterFixture fixture, RequestCqrsRf3Callers administrator,
        NativeAnnPublicColdSeed seed, AnnMaintenanceRequest pin, CancellationToken token)
    {
        var denied = seed.Reader.Principal with
        {
            PolicyEpoch = checked(seed.Reader.Principal.PolicyEpoch + McpCallerProtocol.EpochIncrement),
            Grants = [new(seed.Scenario.Partition.DatabaseId, NativeAnnMaintenanceRf3Scenario.Collection, Denied)]
        };
        var persistedDenied = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(
            Guid.NewGuid(), denied, token));
        await Assert.That(NativeSerialization.Serialize(persistedDenied).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(denied))).IsTrue();
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, seed.Reader.Secret,
            async caller =>
            {
                var request = new ApproximateSearchRequest(Version, seed.Scenario.Search(), pin.Consumer, pin.IndexGeneration);
                var result = await global::KeyLoad.Client.ApproximateSearchClient.ApproximateSearchAsync(caller.Sdk, request, token);
                await Assert.That(result.IsSuccess).IsFalse();
                await Assert.That(result.Value).IsNull();
                await Assert.That(result.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
                _ = await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(Tool,
                    request, token), ErrorCode.PermissionDenied, dispatched: true);
                return true;
            }, token);
        await NativeAnnPublicColdAssertions.StateAsync(administrator, seed, token);
        var repaired = persistedDenied with
        {
            PolicyEpoch = checked(persistedDenied.PolicyEpoch + McpCallerProtocol.EpochIncrement),
            Grants = [new(seed.Scenario.Partition.DatabaseId, NativeAnnMaintenanceRf3Scenario.Collection, Healthy)]
        };
        var persistedRepaired = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(
            Guid.NewGuid(), repaired, token));
        await Assert.That(NativeSerialization.Serialize(persistedRepaired).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(repaired))).IsTrue();
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, seed.Reader.Secret,
            async caller =>
            {
                await AnnPublicRf3Assertions.AllPathsAsync(caller.Sdk, caller.Mcp, seed.Scenario, pin,
                    empty: false, token);
                return true;
            }, token);
        await NativeAnnPublicColdAssertions.StateAsync(administrator, seed, token);
        return seed with
        {
            Reader = seed.Reader with
            {
                Principal = persistedRepaired
            }
        };
    }
}
