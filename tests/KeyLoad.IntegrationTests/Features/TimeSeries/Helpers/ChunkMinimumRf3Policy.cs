using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMinimumRf3Policy
{
    private const long EpochStep = 1;
    private const string PrivateValue = "private-marker";
    private const string FieldDenied = "The query requires a protected field-use grant.";

    internal static Task<bool> RunAsync(TwoRf3MembershipWave wave, ChunkMinimumRf3State state, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, state.Nodes.First(), state.Administrator,
            administrator => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, state.Nodes.Last(), state.Reader.Secret,
                async reader =>
                {
                    var permitted = state.CurrentReader;
                    await ConfigureAsync(wave, state, permitted with
                    {
                        Grants = [],
                        PolicyEpoch = checked(permitted.PolicyEpoch + EpochStep)
                    }, token);
                    await TrialAsync(reader, administrator, state, privateField: false,
                        ChunkMinimumRf3Refusals.ScopeDenied, token);
                    await ConfigureAsync(wave, state, permitted with
                    {
                        PolicyEpoch = checked(state.CurrentReader.PolicyEpoch + EpochStep)
                    }, token);
                    await HealthyAsync(reader, administrator, state, token);
                    await ConfigureAsync(wave, state, permitted with
                    {
                        FieldGrants = [],
                        PolicyEpoch = checked(state.CurrentReader.PolicyEpoch + EpochStep)
                    }, token);
                    await TrialAsync(reader, administrator, state, privateField: true, FieldDenied, token);
                    await ConfigureAsync(wave, state, permitted with
                    {
                        PolicyEpoch = checked(state.CurrentReader.PolicyEpoch + EpochStep)
                    }, token);
                    await HealthyAsync(reader, administrator, state, token);
                    return true;
                }, token), token);

    private static async Task ConfigureAsync(TwoRf3MembershipWave wave, ChunkMinimumRf3State state,
        PrincipalRecord principal, CancellationToken token)
    {
        foreach (var node in new[] { TwoRf3MembershipProtocol.Node1, TwoRf3MembershipProtocol.Node4 })
        {
            await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, node, state.Administrator, async administrator =>
            {
                await SqlRf3Protocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
                    await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));
                return true;
            }, token);
        }
        state.CurrentReader = principal;
    }

    private static async Task TrialAsync(RequestCqrsRf3Callers reader, RequestCqrsRf3Callers administrator,
        ChunkMinimumRf3State state, bool privateField, string detail, CancellationToken token)
    {
        foreach (var window in state.Windows)
        {
            foreach (var official in new[] { false, true })
            {
                var invalid = window.Request(state.Partition) with
                {
                    MinimumToken = window.Minimum with { Incarnation = Guid.Empty },
                    TagPointer = privateField ? TimeSeriesRf3Scenario.SecretField : null,
                    TagValue = privateField ? PrivateValue : null
                };
                await ChunkMinimumRf3Pages.ObserveAsync(() => ChunkMinimumRf3Refusals.FailureAsync(
                    reader, invalid, ErrorCode.PermissionDenied, detail, official, token), administrator, state, token);
            }
        }
    }

    private static async Task HealthyAsync(RequestCqrsRf3Callers reader, RequestCqrsRf3Callers administrator,
        ChunkMinimumRf3State state, CancellationToken token)
    {
        foreach (var window in state.Windows)
        {
            foreach (var official in new[] { false, true })
            {
                var request = window.Request(state.Partition) with
                {
                    MinimumToken = window.Minimum,
                    TagPointer = TimeSeriesRf3Scenario.SecretField,
                    TagValue = PrivateValue
                };
                await ChunkMinimumRf3Pages.ObserveAsync(() => ChunkMinimumRf3Pages.ReadAsync(reader,
                    window, request, official, token), administrator, state, token);
            }
        }
    }
}
