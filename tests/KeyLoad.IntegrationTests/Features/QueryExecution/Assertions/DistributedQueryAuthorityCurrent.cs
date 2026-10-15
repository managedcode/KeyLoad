using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class DistributedQueryAuthorityCurrent
{
    internal static async Task ObserveAsync(DistributedQueryAuthorityContext context, DistributedQueryAuthorityState state,
        Func<Task> operation, CancellationToken token)
    {
        var source = await McpCallerAssertions.SdkSuccessAsync(await context.Source.StatusAsync(token));
        var destination = await McpCallerAssertions.SdkSuccessAsync(await context.Destination.StatusAsync(token));
        var sourceOutbox = await McpCallerAssertions.SdkSuccessAsync(await context.Source.OutboxStatusAsync(RemotePartitionQueryRf3Seed.Local, token));
        var destinationOutbox = await McpCallerAssertions.SdkSuccessAsync(await context.Destination.OutboxStatusAsync(RemoteDocumentRf3Protocol.Partition, token));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => DocumentsAsync(context, token), failures);
        await ServerFailureObserver.ObserveAsync(() => DistributedQueryAuthorityReceipts.RequireAsync(context, state, token), failures);
        await ServerFailureObserver.ObserveAsync(() => DocumentsAsync(context, token), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That((await McpCallerAssertions.SdkSuccessAsync(
            await context.Source.StatusAsync(token))).Applied).IsEqualTo(source.Applied), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That((await McpCallerAssertions.SdkSuccessAsync(
            await context.Destination.StatusAsync(token))).Applied).IsEqualTo(destination.Applied), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(sourceOutbox,
            await McpCallerAssertions.SdkSuccessAsync(await context.Source.OutboxStatusAsync(RemotePartitionQueryRf3Seed.Local, token))), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(destinationOutbox,
            await McpCallerAssertions.SdkSuccessAsync(await context.Destination.OutboxStatusAsync(RemoteDocumentRf3Protocol.Partition, token))), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task DocumentsAsync(DistributedQueryAuthorityContext context, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => OneAsync(context.Source,
            new(RemotePartitionQueryRf3Seed.Local, RemoteDocumentRf3Protocol.Collection, RemoteDocumentRf3Protocol.Document),
            RemotePartitionQueryRf3Seed.SourceJson, token), failures);
        await ServerFailureObserver.ObserveAsync(() => OneAsync(context.Destination,
            RemoteDocumentRf3Protocol.Reference, RemoteDocumentRf3Protocol.OriginalJson, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task OneAsync(KeyLoadClient sdk, EntityRef reference, string json, CancellationToken token)
        => await SqlRf3Protocol.EqualAsync(new DocumentResult(reference, DistributedQueryAuthorityProtocol.FirstRevision, json, false, []),
            await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(reference, token)));
}
