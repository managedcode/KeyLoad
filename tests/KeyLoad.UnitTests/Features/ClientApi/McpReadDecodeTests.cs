using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-003: genuine canonical read DTOs and exact no-body contracts.</summary>
internal sealed class McpReadDecodeTests
{
    private const int BodyReadCount = 15;
    private const string WrongCaseRequestKey = "Request";
    private const string ReferenceKey = "reference";
    private const string PrivateMarker = "private-value-that-must-not-appear-in-an-error";
    private static readonly ImmutableArray<string> NoBodyNames =
        [McpCatalogExpectations.QueryCapabilities, McpCatalogExpectations.AdminBackup,
         McpCatalogExpectations.AdminAdmission, McpCatalogExpectations.AdminStatus];

    /// <summary>Exercises all fifteen actual body-bearing read DTOs without creating write identities.</summary>
    [Test]
    public async Task AcMcp003EveryBodyReadPreservesTheCanonicalDto()
    {
        var cases = McpCanonicalTestData.Reads();
        await Assert.That(cases.Length).IsEqualTo(BodyReadCount);
        foreach (var item in cases)
        {
            var decoded = Find(item.Name).Decode(item.Arguments());
            await Assert.That(decoded.CommandId).IsEqualTo(Guid.Empty);
            await Assert.That(decoded.CommandKind).IsNull();
            await Assert.That(decoded.ReadKind).IsEqualTo(Find(item.Name).ReadKind);
            await Assert.That(decoded.Payload.Span.SequenceEqual(item.ExpectedPayload.Span)).IsTrue();
        }
    }

    /// <summary>The four no-body operations accept empty arguments and produce the exact null sentinel.</summary>
    [Test]
    public async Task AcMcp003OnlyNoBodyReadsAcceptEmptyArgumentsAndProduceNativeNullSentinel()
    {
        foreach (var name in NoBodyNames)
        {
            var descriptor = Find(name);
            var absent = descriptor.Decode(null);
            var empty = descriptor.Decode(new Dictionary<string, JsonElement>(StringComparer.Ordinal));
            await Assert.That(NativeSerialization.Deserialize<JsonElement>(absent.Payload.Span).ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(absent.Payload.Span.SequenceEqual(NativeSerialization.Serialize(
                JsonSerializer.Deserialize<JsonElement>(McpCanonicalTestData.NullJson)))).IsTrue();
            await Assert.That(empty.Payload.Span.SequenceEqual(absent.Payload.Span)).IsTrue();
            var invalid = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [McpCanonicalTestData.RequestKey] = JsonSerializer.SerializeToElement<object?>(null)
            };
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => descriptor.Decode(invalid)).Code)
                .IsEqualTo(ErrorCode.Validation);
        }
    }

    /// <summary>Malformed outer keys fail even when the supplied dictionary uses case-insensitive lookup.</summary>
    [Test]
    public async Task AcMcp003BodyReadsRejectAbsentNullUnknownAndWrongCaseOuterKeys()
    {
        foreach (var item in McpCanonicalTestData.Reads())
        {
            var descriptor = Find(item.Name);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => descriptor.Decode(null)).Code).IsEqualTo(ErrorCode.Validation);
            var invalid = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [WrongCaseRequestKey] = item.Request
            };
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => descriptor.Decode(invalid)).Code).IsEqualTo(ErrorCode.Validation);
            invalid = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [McpCanonicalTestData.RequestKey] = JsonSerializer.SerializeToElement<object?>(null)
            };
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => descriptor.Decode(invalid)).Code).IsEqualTo(ErrorCode.Validation);
            invalid = new Dictionary<string, JsonElement>(item.Arguments(), StringComparer.Ordinal)
            {
                [McpCanonicalTestData.UnknownKey] = JsonSerializer.SerializeToElement(McpCanonicalTestData.Principal)
            };
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => descriptor.Decode(invalid)).Code).IsEqualTo(ErrorCode.Validation);
        }
    }

    /// <summary>Canonical member and constructor validation expose a fixed safe error without caller data.</summary>
    [Test]
    public async Task AcMcp003UnknownDtoMembersAndMissingNestedRequiredFieldsUseSafeValidation()
    {
        var item = McpCanonicalTestData.Reads()[0];
        var request = JsonNode.Parse(item.Request.GetRawText())!.AsObject();
        request[McpCanonicalTestData.UnknownKey] = PrivateMarker;
        var arguments = item.Arguments();
        arguments[McpCanonicalTestData.RequestKey] = JsonSerializer.SerializeToElement(request);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => Find(item.Name).Decode(arguments));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(failure.Message.Contains(PrivateMarker, StringComparison.Ordinal)).IsFalse();
        request.Remove(McpCanonicalTestData.UnknownKey);
        request[ReferenceKey] = new JsonObject();
        arguments[McpCanonicalTestData.RequestKey] = JsonSerializer.SerializeToElement(request);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => Find(item.Name).Decode(arguments)).Code)
            .IsEqualTo(ErrorCode.Validation);
    }

    private static McpOperationDescriptor Find(string name)
    {
        if (!McpOperationCatalog.TryGet(name, out var descriptor))
        {
            throw new InvalidOperationException(name);
        }
        return descriptor!;
    }
}
