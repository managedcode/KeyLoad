using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateColdCallers
{
    internal static Task<T> RunAsync<T>(FunctionalGateFixture fixture, FunctionalGateColdState state,
        Func<FunctionalGateContext, Task<T>> action, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, fixture.NodeNames[0], fixture.AdminKey, administrator =>
            RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, fixture.NodeNames[^1], state.Writer.Secret, writer =>
                RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, fixture.NodeNames[0], state.Reader.Secret, reader =>
                    RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, fixture.NodeNames[^1], state.QueueOperator.Secret, queueOperator =>
                        action(new(administrator, writer, reader, state.Partition, state.Anchor, FunctionalGateColdProtocol.Resources)
                        {
                            ReferenceBindings = state.ReferenceBindings,
                            QueueAdministrator = queueOperator,
                            WriterPrincipalId = state.Writer.Principal.Id,
                            ReaderPrincipalId = state.Reader.Principal.Id,
                            MutableDocumentCollection = FunctionalGateColdProtocol.Resource("docs", 1),
                            VoterCount = fixture.NodeNames.Length
                        }), token), token), token), token);
}
