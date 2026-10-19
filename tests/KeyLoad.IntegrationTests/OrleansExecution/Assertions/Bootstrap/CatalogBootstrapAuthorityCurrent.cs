using System.Collections.Immutable;
using System.Reflection;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.OrleansExecution.CatalogBootstrapAuthorityProtocol;

namespace KeyLoad.IntegrationTests.Features.OrleansExecution;

internal static class CatalogBootstrapAuthorityCurrent
{
    private const string ReferencesProperty = "CompositionReferences";
    private const string MissingReferencesContract = "The execution receipt privacy contract is missing.";
    private static readonly PropertyInfo PrivateReferences = typeof(MutationReceipt)
        .GetProperty(ReferencesProperty, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(MissingReferencesContract);
    internal static async Task RunAsync(ClusterFixture fixture, CatalogBootstrapAuthorityState state, CancellationToken token)
    {
        foreach (var node in P.Nodes)
        {
            await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, node, state.AdministratorKey, async administrator =>
            {
                await administrator.AssertFreshRoutingReadyAsync(token);
                await ReplayAsync(administrator, state.Original, state.Receipt, token);
                if (state.HealthyCommand is not null && state.HealthyReceipt is not null)
                {
                    await ReplayAsync(administrator, state.HealthyCommand, state.HealthyReceipt, token);
                }
                await StatusAsync(administrator, state, node, token);
                return true;
            }, token);
            await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, node, state.ReaderKey, async reader =>
            {
                var reference = new EntityRef(state.Partition, P.Collection, P.Document);
                DocumentReadResult expectedDocument = new(new(reference,
                    state.HealthyReceipt is null ? P.OriginalRevision : P.HealthyRevision,
                    state.HealthyReceipt is null ? P.OriginalJson : P.HealthyJson, false, []));
                await SqlClusterProtocol.EqualAsync(expectedDocument, await McpCallerAssertions.SdkSuccessAsync(
                    await reader.Sdk.GetAsync(reference, state.Receipt.Token, token)));
                await SqlClusterProtocol.EqualAsync(expectedDocument, (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
                    await reader.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, state.Receipt.Token), token))).Value);
                await CatalogBootstrapAuthorityDenied.ReaderAdministrationAsync(reader, state, token);
                return true;
            }, token);
        }
    }

    internal static async Task ReceiptAsync(CommandRequest command, CommitReceipt receipt,
        long revision)
    {
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(receipt, command, [new(P.Put, P.Collection, P.Document, revision)]);
        await Assert.That(receipt.Mutations.All(mutation => PrivateReferences.GetValue(mutation) is ImmutableArray<EntityRef> { IsEmpty: true })).IsTrue();
    }

    private static async Task ReplayAsync(RequestCqrsClusterCallers caller, CommandRequest command,
        CommitReceipt receipt, CancellationToken token)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token));
        var mcp = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value;
        await SqlClusterProtocol.EqualAsync(receipt, sdk);
        await SqlClusterProtocol.EqualAsync(receipt, mcp);
        await Assert.That(sdk.Mutations.All(mutation => PrivateReferences.GetValue(mutation) is ImmutableArray<EntityRef> { IsEmpty: true })).IsTrue();
        await Assert.That(mcp.Mutations.All(mutation => PrivateReferences.GetValue(mutation) is ImmutableArray<EntityRef> { IsEmpty: true })).IsTrue();
    }

    private static async Task StatusAsync(RequestCqrsClusterCallers caller, CatalogBootstrapAuthorityState state,
        string node, CancellationToken token)
    {
        var status = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
        await Assert.That(Guid.TryParse(status.NodeId, out var identity) && identity != Guid.Empty).IsTrue();
        if (state.PhysicalOwners.TryGetValue(node, out var original))
        {
            await Assert.That(status.NodeId).IsEqualTo(original);
        }
        else
        {
            state.PhysicalOwners.Add(node, status.NodeId);
        }
        await Assert.That(status.SiloCount).IsEqualTo(P.Nodes.Length);
        await Assert.That(status.RoutingReady).IsTrue();
        await Assert.That(status.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(status.ProcessId).IsGreaterThan(P.EmptyCount);
        await Assert.That(status.ReadGeneration).IsGreaterThanOrEqualTo(P.EmptyPosition);
    }
}
