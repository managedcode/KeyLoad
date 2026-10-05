using System.Text.Json;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClientApi;

namespace KeyLoad.UnitTests.Features.BlobStorage;

/// <summary>AC-BLOB-006: independent native catalog, schemas and agent hints match the ten accepted operations.</summary>
internal sealed class BlobAgentCatalogTests
{
    private const int BlobCount = 10;
    private const int PublicCount = 67;
    private const string AggregateReplayTool = "keyload_streams_replay";
    private const string AggregateReplayRoute = "/v1/streams/replay";
    private const string SampleRetentionTool = "keyload_series_retention";
    private const string SampleRetentionRoute = "/v1/series/retention";
    private const string Request = "request";
    private const string Stream = "stream";
    private const string ReducerVersion = "reducerVersion";
    private const string StateSchemaVersion = "stateSchemaVersion";
    private const string FromBeginning = "fromBeginning";
    private const string MaximumEvents = "maximumEvents";
    private const string Partition = "partition";
    private const string Set = "set";
    private const string SeriesId = "seriesId";
    private const string AdditionalProperties = "additionalProperties";
    private const string Receipt = "receipt";
    private const string Value = "value";
    private const string Metadata = "metadata";
    private const string Offset = "offset";
    private const string Bytes = "bytes";
    private const string Items = "items";
    private const string NextAfterId = "nextAfterId";
    private const string Encoding = "contentEncoding";
    private const string Base64 = "base64";
    private const string Revision = "revision";
    private const string VersionId = "versionId";
    private const string PartCount = "partCount";
    private const string IntegrityHash = "integrityHash";
    private const string UpdatedAt = "updatedAt";
    private const string Deleted = "deleted";
    private const string DeclaredLength = "declaredLength";
    private const string NextOrdinal = "nextOrdinal";
    private const string StoredBytes = "storedBytes";
    private const string ExpiresAt = "expiresAt";
    private const string Status = "status";
    private const string DeletedParts = "deletedParts";
    private const string RemainingParts = "remainingParts";
    private const string ReleasedBytes = "releasedBytes";
    private const string Complete = "complete";

    /// <summary>Every frozen name resolves once, routes exactly and preserves the accepted read/write hints.</summary>
    [Test]
    public async Task AcBlob006TenExactToolsRetainTheirContractsInThePublicCatalog()
    {
        var cases = BlobAgentCases.All();
        await Assert.That(cases.Length).IsEqualTo(BlobCount);
        await Assert.That(McpOperationCatalog.Entries.Length).IsEqualTo(PublicCount);
        foreach (var item in cases)
        {
            var descriptor = Find(item.Name);
            await Assert.That(McpOperationCatalog.Entries.Count(entry => entry.Name == item.Name)).IsEqualTo(1);
            await Assert.That(descriptor.Route).IsEqualTo(item.Route);
            await Assert.That(descriptor.ReadKind).IsEqualTo(item.ReadKind);
            await Assert.That(descriptor.CommandKind).IsEqualTo(item.CommandKind);
            await Assert.That(descriptor.ReadOnly).IsEqualTo(item.ReadKind.HasValue);
            await Assert.That(descriptor.Idempotent).IsTrue();
            await Assert.That(descriptor.Destructive).IsEqualTo(item.Name is BlobAgentCases.Complete or BlobAgentCases.Delete or BlobAgentCases.Reclaim);
            await Assert.That(McpOperationCatalog.TryGet(item.Name.ToUpperInvariant(), out _)).IsFalse();
            await Assert.That(descriptor.Description.Contains(McpToolDescriptions.StableRetry, StringComparison.Ordinal))
                .IsEqualTo(item.CommandKind.HasValue);
        }
    }

    /// <summary>The aggregate replay and retention read tools have explicit independent route and DTO inventories.</summary>
    [Test]
    public async Task AcMcp001AggregateReplayAndSampleRetentionRemainExactPublicReadTools()
    {
        var cases = new[]
        {
            (AggregateReplayTool, AggregateReplayRoute, GrainReadKind.AggregateReplay,
                new[] { Stream, ReducerVersion, StateSchemaVersion, FromBeginning, MaximumEvents }),
            (SampleRetentionTool, SampleRetentionRoute, GrainReadKind.SampleRetention,
                new[] { Partition, Set, SeriesId })
        };
        foreach (var item in cases)
        {
            var descriptor = Find(item.Item1);
            await Assert.That(McpOperationCatalog.Entries.Count(entry => entry.Name == item.Item1)).IsEqualTo(1);
            await Assert.That(descriptor.Route).IsEqualTo(item.Item2);
            await Assert.That(descriptor.ReadKind).IsEqualTo(item.Item3);
            await Assert.That(descriptor.CommandKind).IsNull();
            await Assert.That(descriptor.ReadOnly).IsTrue();
            await Assert.That(descriptor.Idempotent).IsTrue();
            await Assert.That(descriptor.Destructive).IsFalse();
            var schema = descriptor.InputSchema;
            await Assert.That(schema.GetProperty(AdditionalProperties).ValueKind).IsEqualTo(JsonValueKind.False);
            await Assert.That(Names(schema.GetProperty(McpSchemaInspector.Properties))).IsEquivalentTo(new[] { Request });
            var requestSchema = McpSchemaInspector.DefinedRequest(schema);
            await Assert.That(Names(requestSchema.GetProperty(McpSchemaInspector.Properties)))
                .IsEquivalentTo(item.Item4);
            await Assert.That(requestSchema.GetProperty(AdditionalProperties).ValueKind).IsEqualTo(JsonValueKind.False);
        }
    }

    /// <summary>Agent request schemas expose only canonical request DTOs, strict part bytes and documented bounded defaults.</summary>
    [Test]
    public async Task AcBlob006CanonicalInputSchemasHaveNoCallerAuthorityAndStrictPartBytes()
    {
        foreach (var item in BlobAgentCases.All())
        {
            var schema = Find(item.Name).InputSchema;
            await Assert.That(schema.GetProperty(AdditionalProperties).ValueKind).IsEqualTo(JsonValueKind.False);
            await Assert.That(Names(schema.GetProperty(McpSchemaInspector.Properties))).IsEquivalentTo(new[] { BlobAgentCases.Request });
            var request = McpSchemaInspector.DefinedRequest(schema);
            await Assert.That(Names(request.GetProperty(McpSchemaInspector.Properties))).IsEquivalentTo(BlobAgentCases.RequestFields(item.Name));
            await Assert.That(request.GetProperty(AdditionalProperties).ValueKind).IsEqualTo(JsonValueKind.False);
        }
        var part = McpSchemaInspector.DefinedRequest(Find(BlobAgentCases.Part).InputSchema)
            .GetProperty(McpSchemaInspector.Properties).GetProperty(Bytes);
        await Assert.That(McpSchemaInspector.HasType(part, McpSchemaInspector.String)).IsTrue();
        await Assert.That(McpSchemaInspector.HasType(part, McpSchemaInspector.Null)).IsFalse();
        await Assert.That(part.GetProperty(Encoding).GetString()).IsEqualTo(Base64);
    }

    /// <summary>Nullable absent reads and command receipt/value wrappers remain distinct from range/list result DTOs.</summary>
    [Test]
    public async Task AcBlob006OutputSchemasRetainExactTypedResultAndAbsentReadSemantics()
    {
        foreach (var item in BlobAgentCases.All())
        {
            var root = Find(item.Name).OutputSchema;
            var success = root.GetProperty(McpSchemaInspector.OneOf)[0];
            var wrapper = success.GetProperty(McpSchemaInspector.Properties).GetProperty(McpSchemaInspector.Result);
            var nullable = item.Name is BlobAgentCases.Metadata or BlobAgentCases.UploadInfo;
            await Assert.That(McpSchemaInspector.HasType(wrapper, McpSchemaInspector.Null)).IsEqualTo(nullable);
            var result = root.GetProperty(McpSchemaInspector.Defs).GetProperty(McpSchemaInspector.Result);
            if (item.CommandKind.HasValue)
            {
                await Assert.That(Names(result.GetProperty(McpSchemaInspector.Properties))).IsEquivalentTo(new[] { Receipt, Value });
                var value = Resolve(root, result.GetProperty(McpSchemaInspector.Properties).GetProperty(Value));
                await Assert.That(Names(value.GetProperty(McpSchemaInspector.Properties))).IsEquivalentTo(ValueFields(item.Name));
            }
        }
        await Assert.That(ResultFields(BlobAgentCases.Range)).IsEquivalentTo(new[] { Metadata, Offset, Bytes });
        await Assert.That(ResultFields(BlobAgentCases.List)).IsEquivalentTo(new[] { Items, NextAfterId });
    }

    private static string[] ResultFields(string name) => Names(Find(name).OutputSchema.GetProperty(McpSchemaInspector.Defs)
        .GetProperty(McpSchemaInspector.Result).GetProperty(McpSchemaInspector.Properties));

    private static string[] Names(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];

    private static JsonElement Resolve(JsonElement root, JsonElement node) => node.TryGetProperty(McpSchemaInspector.Ref, out var reference)
        ? McpSchemaInspector.Resolve(root, reference.GetString()!) : node;

    private static string[] ValueFields(string name) => name switch
    {
        BlobAgentCases.Complete or BlobAgentCases.Delete =>
            [BlobAgentCases.BlobMember, Revision, VersionId, BlobAgentCases.LengthMember, PartCount, IntegrityHash, BlobAgentCases.AccessMember, UpdatedAt, Deleted],
        BlobAgentCases.Reclaim => [BlobAgentCases.UploadIdMember, DeletedParts, RemainingParts, ReleasedBytes, Complete],
        _ => [BlobAgentCases.BlobMember, BlobAgentCases.UploadIdMember, DeclaredLength, BlobAgentCases.ExpectedRevisionMember,
            NextOrdinal, StoredBytes, ExpiresAt, Status, IntegrityHash]
    };

    internal static McpOperationDescriptor Find(string name) => McpOperationCatalog.Entries.Single(entry => entry.Name == name);
}
