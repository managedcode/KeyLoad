using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateColdWorkloads
{
    internal static Task<FunctionalGateColdState> SeedAsync(FunctionalGateFixture fixture,
        FunctionalGateColdState state, CancellationToken token)
        => FunctionalGateColdCallers.RunAsync(fixture, state, async context =>
        {
            await FunctionalGateColdWorkloadProvision.InventoryAsync(state);
            await FunctionalGateColdRelationalClosure.OriginalsAsync(context, state, token);
            var proofs = new List<FunctionalGateColdWorkload>();
            for (var index = 0; index < state.AdditionalIdentities.Length; index++)
            {
                var identity = state.AdditionalIdentities[index];
                var collection = FunctionalGateColdWorkloadProvision.Resource(index);
                var proof = await RunAsync(fixture, context, identity,
                    caller => SeedOneAsync(caller, state, identity, collection, token), token);
                proofs.Add(proof);
            }
            return state with
            {
                Workloads = [.. proofs]
            };
        }, token);

    private static async Task<FunctionalGateColdWorkload> SeedOneAsync(FunctionalGateContext context,
        FunctionalGateColdState state, McpPersistedIdentity identity, string collection, CancellationToken token)
    {
        var json = JsonSerializer.Serialize(new
        {
            principal = identity.Principal.Id,
            stage = "eight-user-cold"
        }, JsonDefaults.Options);
        var command = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new PutDocument(collection, "original", json, 0)]);
        var receipt = await context.SdkAsync(context.Writer.Sdk.CommitAsync(command, token));
        await FunctionalGateRelationalAssertions.ReceiptAsync(receipt, command.CommandId, state.Partition,
            [new("putDocument", collection, "original", 1)]);
        var retained = new FunctionalGateColdWorkload(identity, command, receipt,
            new(new(new(state.Partition, collection, "original"), 1, json, false, [])));
        await ReplayAsync(context, retained, token);
        foreach (var model in state.Models)
        {
            await FunctionalGateColdReadAssertions.VerifyAsync(context, model, token);
        }
        await FunctionalGateColdRelationalClosure.VerifyAsync(context, state, token);
        return retained;
    }

    internal static async Task VerifyAsync(FunctionalGateFixture fixture, FunctionalGateContext context,
        FunctionalGateColdState state, CancellationToken token)
    {
        await FunctionalGateColdWorkloadProvision.InventoryAsync(state);
        await FunctionalGateColdRelationalClosure.OriginalsAsync(context, state, token);
        await Assert.That(state.Workloads.Length).IsEqualTo(FunctionalGateColdWorkloadProvision.AdditionalIdentities);
        foreach (var proof in state.Workloads)
        {
            await RunAsync(fixture, context, proof.Identity, async caller =>
            {
                await ReplayAsync(caller, proof, token);
                foreach (var model in state.Models)
                {
                    await FunctionalGateColdReadAssertions.VerifyAsync(caller, model, token);
                }
                await FunctionalGateColdRelationalClosure.VerifyAsync(caller, state, token);
                await FunctionalGateColdWorkloadAuthorization.RunAsync(caller, proof, state, token);
                return true;
            }, token);
        }
    }

    internal static async Task ReplayAsync(FunctionalGateContext context, FunctionalGateColdWorkload proof,
        CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(proof.Receipt, await context.SdkAsync(context.Writer.Sdk.CommitAsync(proof.Command, token)));
        await SqlRf3Protocol.EqualAsync(proof.Receipt, await context.McpAsync<CommitReceipt>(context.Writer,
            McpCallerTools.DocumentsCommit, proof.Command, token));
        var reference = proof.Expected.Document!.Reference;
        await SqlRf3Protocol.EqualAsync(proof.Expected, await context.SdkAsync(context.Reader.Sdk.GetAsync(reference, token)));
        await SqlRf3Protocol.EqualAsync(proof.Expected, await context.McpAsync<DocumentReadResult>(context.Reader,
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token));
    }

    private static Task<T> RunAsync<T>(FunctionalGateFixture fixture, FunctionalGateContext original,
        McpPersistedIdentity identity, Func<FunctionalGateContext, Task<T>> action, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, fixture.NodeNames[^1], identity.Secret,
            caller => action(original with
            {
                Writer = caller,
                Reader = caller,
                WriterPrincipalId = identity.Principal.Id,
                ReaderPrincipalId = identity.Principal.Id
            }), token);
}
