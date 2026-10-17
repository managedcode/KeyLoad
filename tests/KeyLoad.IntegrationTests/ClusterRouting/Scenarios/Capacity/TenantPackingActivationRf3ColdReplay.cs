using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantPackingActivationRf3ColdReplay
{
    private const long FirstCurrentRevision = 3;
    private const long SecondCurrentRevision = 2;
    private const long FirstHealthyRevision = 4;

    internal static async Task RunAsync(TwoRf3MembershipWave wave,
        TenantPackingActivationRf3State state, CancellationToken token)
    {
        await NativeActivationRf3ColdOutcome.VerifyAsync(wave, state.First,
            state.FirstSeed, state.FirstReceipt, token);
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            wave.Profile.AdminKey, async administrator =>
            {
                return await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application,
                    TwoRf3MembershipProtocol.Node2, state.First.Secret, async first =>
                    {
                        return await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application,
                            TwoRf3MembershipProtocol.Node3, state.Second.Secret, async second =>
                            {
                                await CurrentAsync(administrator, first, second, state,
                                    FirstCurrentRevision, RequestCqrsRf3Protocol.DocumentJson, token);
                                await ReplayAsync(administrator, first, second, first, state,
                                    state.FirstSeed, state.FirstReceipt, token);
                                await ReplayAsync(administrator, first, second, second, state,
                                    state.SecondSeed, state.SecondReceipt, token);
                                var reference = Reference(state.First);
                                await RequestCqrsFaultReplayContinuation.VerifyAsync(first, administrator.Sdk,
                                    reference, state.FirstSeed, state.FirstReceipt,
                                    RequestCqrsRf3Protocol.ChangedDocumentJson, FirstCurrentRevision,
                                    RequestCqrsRf3Protocol.DocumentJson, token);
                                await CurrentAsync(administrator, first, second, state,
                                    FirstHealthyRevision, RequestCqrsRf3Protocol.ChangedDocumentJson, token);
                                return true;
                            }, token);
                    }, token);
            }, token);
    }

    private static async Task ReplayAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers first, RequestCqrsRf3Callers second, RequestCqrsRf3Callers caller,
        TenantPackingActivationRf3State state, CommandRequest command, CommitReceipt expected,
        CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.CommitAsync(command, token)));
        await CurrentAsync(administrator, first, second, state,
            FirstCurrentRevision, RequestCqrsRf3Protocol.DocumentJson, token);
        await SqlRf3Protocol.EqualAsync(expected,
            (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await caller.Mcp.CallAsync(
                McpCallerTools.DocumentsCommit, command, token))).Value);
        await CurrentAsync(administrator, first, second, state,
            FirstCurrentRevision, RequestCqrsRf3Protocol.DocumentJson, token);
    }

    private static async Task CurrentAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers first, RequestCqrsRf3Callers second, TenantPackingActivationRf3State state,
        long firstRevision, string firstJson, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.FirstPlacement,
            await TenantPackingActivationRf3Admission.ReadAsync(administrator, state.First.Partition, token));
        await SqlRf3Protocol.EqualAsync(state.SecondPlacement,
            await TenantPackingActivationRf3Admission.ReadAsync(administrator, state.Second.Partition, token));
        await RequestCqrsFaultReplayContinuation.DocumentAsync(first, Reference(state.First),
            firstRevision, firstJson, token);
        await RequestCqrsFaultReplayContinuation.DocumentAsync(second, Reference(state.Second),
            SecondCurrentRevision, RequestCqrsRf3Protocol.ChangedDocumentJson, token);
    }

    private static EntityRef Reference(RequestCqrsPhaseFaultIdentity identity)
        => new(identity.Partition, RequestCqrsRf3Protocol.AdminCollection, RequestCqrsRf3Protocol.DocumentId);
}
