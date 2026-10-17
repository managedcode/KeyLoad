using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ClusterRouting;
using Microsoft.Extensions.Options;
using static KeyLoad.UnitTests.Features.DocumentStorage.DocumentAuthorityIntersectionNativeProtocol;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class DocumentAuthorityIntersectionNativeSupport
{
    internal static PrincipalRecord Principal(long epoch, ImmutableArray<string> fields)
        => new(RemoteDocumentNativeFixture.Reader, RemoteDocumentNativeFixture.Tenant,
            [new(RemoteDocumentNativeFixture.Partition.DatabaseId, RemoteDocumentNativeFixture.Collection,
                Capability.DocumentsRead)], fields)
        {
            PolicyEpoch = epoch
        };

    internal static async Task PrincipalAsync(PhysicalShardCatalogFixture owner, PrincipalRecord principal)
    {
        var result = owner.Database.Apply(RemoteDocumentNativeFixture.Operation(owner, OperationKind.ConfigurePrincipal,
            Guid.NewGuid(), new ConfigurePrincipalRequest(principal)));
        await Assert.That(result.Error).IsNull();
        await EqualAsync(principal, result.Get<PrincipalRecord>());
    }

    internal static async Task ResourceAsync(PhysicalShardCatalogFixture owner, params SensitiveFieldPolicy[] fields)
    {
        var partition = RemoteDocumentNativeFixture.Partition;
        var current = Resource(owner);
        var next = current with
        {
            SchemaVersion = current.SchemaVersion + EpochStep,
            FieldPolicies = [.. fields]
        };
        var result = owner.Database.Apply(RemoteDocumentNativeFixture.Operation(owner, OperationKind.ConfigureResource,
            Guid.NewGuid(), new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, next)
            {
                ExpectedSchemaVersion = current.SchemaVersion
            }));
        await Assert.That(result.Error).IsNull();
        await EqualAsync(next, result.Get<ResourceDefinition>());
    }

    internal static ResourceDefinition Resource(PhysicalShardCatalogFixture owner)
        => owner.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            RemoteDocumentNativeFixture.Partition.TenantId, RemoteDocumentNativeFixture.Partition.DatabaseId,
            RemoteDocumentNativeFixture.Collection))) ?? throw new InvalidOperationException(Missing);

    internal static async Task<(ReplicatedOperation Operation, CommitReceipt Receipt)> WriteAsync(
        RemoteDocumentNativeFixture fixture, params PutDocument[] rows)
    {
        var id = Guid.NewGuid();
        var operation = RemoteDocumentNativeFixture.Operation(fixture.Destination, OperationKind.Batch, id,
            new CommandRequest(id, RemoteDocumentNativeFixture.Partition, [.. rows]));
        var result = fixture.Destination.Database.Apply(operation);
        await Assert.That(result.Error).IsNull();
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        return (operation, receipt);
    }

    internal static async Task<OwnedDocumentReadResultV1> ReadAsync(RemoteDocumentNativeFixture fixture, string id)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var source = fixture.Source.Database;
        var target = fixture.Destination.Database;
        var sourcePosition = fixture.Source.Store.Position;
        var targetPosition = fixture.Destination.Store.Position;
        var targetEpoch = fixture.Destination.Store.Read(view => target.Principal(view,
            RemoteDocumentNativeFixture.Reader, target.EvaluationClock.GetUtcNow()).PolicyEpoch);
        var reference = Reference(id);
        var sourceWork = new ReadExecutionBudget(Options.Create(source.Limits), source.EvaluationClock, token);
        var expiry = source.EvaluationClock.GetUtcNow() + TimeSpan.FromSeconds(source.Limits.QueryDeadlineSeconds);
        var frame = source.CaptureRemoteDocumentReadV2(RemoteDocumentNativeFixture.Reader, reference, expiry, sourceWork)
            ?? throw new InvalidOperationException(Missing);
        var maximumBytes = Math.Min(target.Limits.MaxBatchBytes, target.Limits.MaxQueryReadBytes);
        var maximumRecords = Math.Min(target.Limits.MaxBatchMutations, target.Limits.MaxScanRecords);
        var targetWork = new ReadExecutionBudget(Options.Create(target.Limits), target.EvaluationClock, token);
        var grant = targetWork.CreateReadGrant(maximumBytes, maximumRecords);
        var request = new OwnedDocumentReadRequestV2(frame, new(reference),
            PhysicalOwnerDirectoryWholeFlow.Destination.Owner, maximumBytes, maximumRecords, target.Limits.MaxBatchBytes);
        var result = target.ReadOwnedDocumentV2(RemoteDocumentNativeFixture.Reader, request, targetWork, grant);
        targetWork.CheckResult(result);
        await Assert.That(result.PolicyEpoch).IsEqualTo(targetEpoch);
        source.ValidateRemoteDocumentReadV2(frame, sourceWork);
        sourceWork.Check();
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(sourcePosition);
        await Assert.That(fixture.Destination.Store.Position).IsEqualTo(targetPosition);
        return result;
    }

    internal static EntityRef Reference(string id)
        => new(RemoteDocumentNativeFixture.Partition, RemoteDocumentNativeFixture.Collection, id);

    internal static async Task RequireDocumentAsync(RemoteDocumentNativeFixture fixture, string id,
        string json, bool redacted, ImmutableArray<string> visible)
    {
        var actual = await ReadAsync(fixture, id);
        await EqualAsync(new DocumentResult(Reference(id), Revision, json, redacted, visible), actual.Document);
    }

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(NativeSerialization.Serialize(actual).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
}
