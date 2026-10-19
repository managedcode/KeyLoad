using System.Text.Json;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.OrleansExecution;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class PublicCommandTransportTests
{
    [Test]
    [Arguments(PublicShape.NullMutation)]
    [Arguments(PublicShape.NullPatch)]
    [Arguments(PublicShape.NullGrant)]
    public async Task PublicMcpNullElementsRouteAndRetainOneDurableDomainOutcome(PublicShape shape)
    {
        using var database = new TestDatabase();
        var operation = PublicNormalizationFixture.Create(database, shape);
        var decoded = Decode(database, operation);
        await Assert.That(decoded.CommandId).IsEqualTo(operation.CommandId);
        await Assert.That(decoded.CommandKind).IsEqualTo(operation.Value!.Kind);
        var codec = new GrainRequestCodec(database.Database.Store.Identity.Incarnation, database.Database.Store.Identity.SigningKey,
            database.Database.OperationLimitsOptions, TimeProvider.System, UnitRoutingOptions.Routing());
        var request = codec.Verify(codec.CreateCommand(Guid.NewGuid(), operation.UserId!,
            operation.Value.Kind, operation.CommandId, decoded.Payload));
        var actorKey = GrainPartitionResolver.Resolve(request);
        var executor = new GrainCommandExecutor(database.Database, new EmbeddedCoordinator(database.Database), TimeProvider.System, UnitRoutingOptions.Routing());
        var result = await SignedGrainRequestTestContext.ExecuteAsync(executor, request, actorKey, CancellationToken.None);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(result.SafeDetail).IsEqualTo(PublicNormalizationFixture.Detail(shape));
        var stored = PublicNormalizationFixture.Stored(database, operation);
        await Assert.That(Errors.Code(stored.Result.Problem)).IsEqualTo(result.Error);
        await Assert.That(stored.Result.Problem?.Detail).IsEqualTo(result.SafeDetail);
        var retry = codec.Verify(codec.CreateCommand(Guid.NewGuid(), operation.UserId!,
            operation.Value.Kind, operation.CommandId, decoded.Payload));
        await Assert.That(retry.Envelope.RequestId).IsNotEqualTo(request.Envelope.RequestId);
        var repeated = await SignedGrainRequestTestContext.ExecuteAsync(executor, retry, actorKey, CancellationToken.None);
        await Assert.That(repeated).IsEqualTo(result);
        await Assert.That(PublicNormalizationFixture.Stored(database, operation)).IsEqualTo(stored);
    }

    private static McpDecodedOperation Decode(TestDatabase database, Command<DatabaseCommand> operation)
    {
        using var document = JsonDocument.Parse(PublicNormalizationFixture.PublicJson(operation));
        var arguments = new Dictionary<string, JsonElement>
        {
            [McpCatalogProtocol.Request] = document.RootElement.Clone()
        };
        if (operation.Value!.Kind == OperationKind.Batch)
        {
            return McpArgumentDecoder.Command<CommandRequest>(arguments, operation.Value!.Kind, value => value.CommandId,
                database.Database.Limits.MaxBatchBytes);
        }
        arguments[McpCatalogProtocol.CommandId] = JsonSerializer.SerializeToElement(operation.CommandId, JsonDefaults.Options);
        return McpArgumentDecoder.HeaderCommand<ConfigurePrincipalRequest>(arguments, operation.Value!.Kind,
            database.Database.Limits.MaxBatchBytes);
    }
}
