using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-002/003/005: canonical write identity and strict adapter arguments.</summary>
internal sealed class McpCommandDecodeTests
{
    private const int CommandCount = 18;

    /// <summary>Exercises all eighteen actual command DTOs with their original identities and complete public fields.</summary>
    [Test]
    public async Task AcMcp002EveryCommandPreservesItsCallerIdAndFullTypedPayload()
    {
        var cases = McpCanonicalTestData.Commands();
        await Assert.That(cases.Length).IsEqualTo(CommandCount);
        foreach (var item in cases)
        {
            var arguments = item.Arguments();
            var count = arguments.Count;
            var decoded = Find(item.Name).Decode(arguments);
            await Assert.That(decoded.CommandId).IsEqualTo(item.CommandId);
            await Assert.That(decoded.ReadKind).IsNull();
            await Assert.That(decoded.CommandKind).IsEqualTo(Find(item.Name).CommandKind);
            _ = await McpNativePayloadAssertions.AssertFullPublicPayload(item, decoded.Payload);
            await Assert.That(arguments.Count).IsEqualTo(count);
            await Assert.That(arguments[McpCanonicalTestData.RequestKey].GetRawText()).IsEqualTo(item.Request.GetRawText());
        }
    }

    /// <summary>Every command rejects an empty DTO or outer command identity.</summary>
    [Test]
    public async Task AcMcp003EveryCommandRejectsItsEmptyStableId()
    {
        foreach (var item in McpCanonicalTestData.Commands())
        {
            var arguments = item.Arguments();
            if (item.IdMember is null)
            {
                arguments[McpCanonicalTestData.CommandKey] = JsonSerializer.SerializeToElement(Guid.Empty, JsonDefaults.Options);
            }
            else
            {
                var request = JsonNode.Parse(item.Request.GetRawText())!.AsObject();
                request[item.IdMember] = JsonValue.Create(Guid.Empty);
                arguments[McpCanonicalTestData.RequestKey] = JsonSerializer.SerializeToElement(request);
            }
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => Find(item.Name).Decode(arguments));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        }
    }

    /// <summary>The four header-identity operations require a valid outer GUID.</summary>
    [Test]
    public async Task AcMcp003HeaderIdToolsRejectMissingAndNullOuterIdentity()
    {
        foreach (var item in McpCanonicalTestData.Commands().Where(item => item.IdMember is null))
        {
            var arguments = item.Arguments();
            arguments.Remove(McpCanonicalTestData.CommandKey);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => Find(item.Name).Decode(arguments)).Code)
                .IsEqualTo(ErrorCode.Validation);
            arguments[McpCanonicalTestData.CommandKey] = JsonSerializer.SerializeToElement<object?>(null);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => Find(item.Name).Decode(arguments)).Code)
                .IsEqualTo(ErrorCode.Validation);
        }
    }

    /// <summary>Every command rejects unexpected outer authority and null request values.</summary>
    [Test]
    public async Task AcMcp003AllCommandsRejectUnknownOrNullRequestArguments()
    {
        foreach (var item in McpCanonicalTestData.Commands())
        {
            var arguments = item.Arguments();
            arguments[McpCanonicalTestData.UnknownKey] = JsonSerializer.SerializeToElement(McpCanonicalTestData.Principal);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => Find(item.Name).Decode(arguments)).Code)
                .IsEqualTo(ErrorCode.Validation);
            arguments.Remove(McpCanonicalTestData.UnknownKey);
            arguments[McpCanonicalTestData.RequestKey] = JsonSerializer.SerializeToElement<object?>(null);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => Find(item.Name).Decode(arguments)).Code)
                .IsEqualTo(ErrorCode.Validation);
        }
    }

    /// <summary>Owned canonical operation bytes outlive the native argument document.</summary>
    [Test]
    public async Task AcMcp003DecodedPayloadSurvivesDisposalOfItsOriginalJsonDocument()
    {
        var item = McpCanonicalTestData.Commands()[0];
        McpDecodedOperation decoded;
        byte[] copiedWhileDocumentWasAlive;
        using (var document = JsonDocument.Parse(item.Request.GetRawText()))
        {
            decoded = Find(item.Name).Decode(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [McpCanonicalTestData.RequestKey] = document.RootElement
            });
            copiedWhileDocumentWasAlive = decoded.Payload.ToArray();
        }
        await Assert.That(decoded.Payload.Span.SequenceEqual(copiedWhileDocumentWasAlive)).IsTrue();
        var actual = await McpNativePayloadAssertions.AssertFullPublicPayload(item, decoded.Payload);
        await Assert.That(((CommandRequest)actual).CommandId).IsEqualTo(item.CommandId);
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
