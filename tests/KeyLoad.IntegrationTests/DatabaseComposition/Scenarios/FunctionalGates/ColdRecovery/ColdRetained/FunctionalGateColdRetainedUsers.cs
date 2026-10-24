namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateColdRetainedUsers
{
    private const int FirstModel = 0;
    private const long OriginalRevision = 1;

    internal static async Task VerifyAsync(FunctionalGateFixture fixture, FunctionalGateContext original,
        FunctionalGateColdState state, Func<FunctionalGateContext, Task> unchanged, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(unchanged);
        await FunctionalGateColdWorkloadProvision.InventoryAsync(state);
        await Assert.That(state.Workloads.Length).IsEqualTo(FunctionalGateColdWorkloadProvision.AdditionalIdentities);
        await Assert.That(state.Workloads.Select(proof => proof.Identity.Principal.Id).Order(StringComparer.Ordinal))
            .IsEquivalentTo(state.AdditionalIdentities.Select(identity => identity.Principal.Id).Order(StringComparer.Ordinal));
        var model = state.Models.Single(model => model.Index == FirstModel);
        var reference = original.Bind(new(state.Partition, FunctionalGateColdProtocol.Resource("docs", FirstModel),
            FunctionalGateColdProtocol.Id));
        var expected = new DocumentReadResult(new(reference, OriginalRevision,
            FunctionalGateColdProtocol.DocumentJson(original.Bind(state.Anchor), model.Blob.Path), false, []));
        await FunctionalGateColdRetainedCalls.RunAsync(fixture, original, state.Writer,
            context => WriterAsync(context, model.Command, expected, unchanged, token), token);
        foreach (var proof in state.Workloads)
        {
            await FunctionalGateColdRetainedCalls.RunAsync(fixture, original, proof.Identity,
                context => WriterAsync(context, proof.Command, proof.Expected, unchanged, token), token);
        }
        await FunctionalGateColdRetainedRows.ReaderAsync(original, state, unchanged, token);
        await FunctionalGateColdRetainedRows.OperatorAsync(original, state, unchanged, token);
    }

    private static async Task WriterAsync(FunctionalGateContext context, CommandRequest original,
        DocumentReadResult expected, Func<FunctionalGateContext, Task> unchanged, CancellationToken token)
    {
        await FunctionalGateColdRetainedRefusals.AllAsync(context, original, unchanged, token);
        await FunctionalGateColdRetainedRows.ReadAsync(context, expected, token);
        await FunctionalGateColdRetainedHealthy.RunAsync(context, expected.Document!.Reference, unchanged, token);
    }
}
