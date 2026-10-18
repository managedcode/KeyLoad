using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateColdCallers
{
    internal static Task<T> RunAsync<T>(FunctionalGateFixture fixture, FunctionalGateColdState state,
        Func<FunctionalGateContext, Task<T>> action, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, fixture.NodeNames[0], fixture.AdminKey, administrator =>
            RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, fixture.NodeNames[^1], state.Writer.Secret, writer =>
                RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, fixture.NodeNames[0], state.Reader.Secret, reader =>
                    RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, fixture.NodeNames[^1], state.QueueOperator.Secret, queueOperator =>
                        action(new(administrator, writer, reader, state.Partition, state.Anchor, FunctionalGateColdProtocol.Resources)
                        {
                            ReferenceBindings = state.ReferenceBindings,
                            QueueAdministrator = queueOperator,
                            WriterPrincipalId = state.Writer.Principal.Id,
                            ReaderPrincipalId = state.Reader.Principal.Id,
                            MutableDocumentCollection = FunctionalGateColdProtocol.Resource("docs", 1),
                            ReplicaCount = fixture.ReplicaCount
                        }), token), token), token), token);
}
