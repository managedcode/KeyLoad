using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativePublicCommandTransportTests
{
    [Test]
    [Arguments(NativePublicShape.NullMutation)]
    [Arguments(NativePublicShape.NullPatch)]
    [Arguments(NativePublicShape.NullGrant)]
    public async Task PublicMcpNullElementsRouteAndRetainOneDurableDomainOutcome(NativePublicShape shape)
    {
        using var database = new TestDatabase();
        var operation = NativePublicNormalizationFixture.Create(database, shape);
        var decoded = Decode(database, operation);
        await Assert.That(decoded.CommandId).IsEqualTo(operation.Id);
        await Assert.That(decoded.CommandKind).IsEqualTo(operation.Kind);
        var codec = new GrainRequestCodec(database.Database, TimeProvider.System);
        var request = codec.Verify(codec.CreateCommand(Guid.NewGuid(), operation.PrincipalId,
            operation.Kind, operation.Id, decoded.Payload));
        var actorKey = GrainPartitionResolver.Resolve(request);
        var executor = new GrainCommandExecutor(database.Database, new EmbeddedCoordinator(database.Database), TimeProvider.System);
        var result = await SignedGrainRequestTestContext.ExecuteAsync(executor, request, actorKey, CancellationToken.None);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(result.SafeDetail).IsEqualTo(NativePublicNormalizationFixture.Detail(shape));
        var stored = NativePublicNormalizationFixture.Stored(database, operation);
        await Assert.That(stored.Result.Error).IsEqualTo(result.Error);
        await Assert.That(stored.Result.SafeDetail).IsEqualTo(result.SafeDetail);
        var retry = codec.Verify(codec.CreateCommand(Guid.NewGuid(), operation.PrincipalId,
            operation.Kind, operation.Id, decoded.Payload));
        await Assert.That(retry.Envelope.RequestId).IsNotEqualTo(request.Envelope.RequestId);
        var repeated = await SignedGrainRequestTestContext.ExecuteAsync(executor, retry, actorKey, CancellationToken.None);
        await Assert.That(repeated).IsEqualTo(result);
        await Assert.That(NativePublicNormalizationFixture.Stored(database, operation)).IsEqualTo(stored);
    }

    private static McpDecodedOperation Decode(TestDatabase database, ReplicatedOperation operation)
    {
        using var document = JsonDocument.Parse(operation.PayloadJson);
        var arguments = new Dictionary<string, JsonElement>
        { [McpCatalogProtocol.Request] = document.RootElement.Clone() };
        if (operation.Kind == OperationKind.Batch)
        {
            return McpArgumentDecoder.Command<CommandRequest>(arguments, operation.Kind, value => value.CommandId,
                database.Database.Limits.MaxBatchBytes);
        }
        arguments[McpCatalogProtocol.CommandId] = JsonSerializer.SerializeToElement(operation.Id, JsonDefaults.Options);
        return McpArgumentDecoder.HeaderCommand<ConfigurePrincipalRequest>(arguments, operation.Kind,
            database.Database.Limits.MaxBatchBytes);
    }
}
