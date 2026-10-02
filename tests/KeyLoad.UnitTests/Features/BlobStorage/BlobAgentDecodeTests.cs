using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BlobStorage;

/// <summary>AC-BLOB-003/006/007: genuine native decoder preserves strict canonical payloads and caller command IDs.</summary>
internal sealed class BlobAgentDecodeTests
{
    private const string InvalidBytes = "not-base64!";
    private const int CommandCount = 6;

    /// <summary>All ten canonical DTOs round-trip byte-for-byte without changing the native arguments.</summary>
    [Test]
    public async Task AcBlob006EveryAgentOperationKeepsExactCanonicalPayloadAndStableCommandIdentity()
    {
        var cases = BlobAgentCases.All();
        await Assert.That(cases.Count(item => item.CommandKind.HasValue)).IsEqualTo(CommandCount);
        foreach (var item in cases)
        {
            var arguments = item.Arguments();
            var original = arguments[BlobAgentCases.Request].GetRawText();
            var decoded = BlobAgentCatalogTests.Find(item.Name).Decode(arguments);
            await Assert.That(decoded.Payload.Span.SequenceEqual(item.Payload.Span)).IsTrue();
            await Assert.That(decoded.CommandId).IsEqualTo(item.CommandKind.HasValue ? BlobAgentCases.CommandId : Guid.Empty);
            await Assert.That(decoded.CommandKind).IsEqualTo(item.CommandKind);
            await Assert.That(decoded.ReadKind).IsEqualTo(item.ReadKind);
            await Assert.That(arguments.Count).IsEqualTo(1);
            await Assert.That(arguments[BlobAgentCases.Request].GetRawText()).IsEqualTo(original);
        }
    }

    /// <summary>Each of the six commands rejects an empty stable identity instead of allocating a replacement.</summary>
    [Test]
    public async Task AcBlob006EveryCommandRejectsEmptyCallerCommandId()
    {
        foreach (var item in BlobAgentCases.All().Where(item => item.CommandKind.HasValue))
        {
            var node = JsonNode.Parse(item.Request.GetRawText())!.AsObject();
            node[BlobAgentCases.CommandIdMember] = JsonValue.Create(Guid.Empty);
            var arguments = item.Arguments();
            arguments[BlobAgentCases.Request] = JsonSerializer.SerializeToElement(node);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => BlobAgentCatalogTests.Find(item.Name).Decode(arguments)).Code)
                .IsEqualTo(ErrorCode.Validation);
        }
    }

    /// <summary>No operation accepts caller authority, unknown request fields or null requests.</summary>
    [Test]
    public async Task AcBlob003UnknownAuthorityAndNullRequestsFailBeforeDispatch()
    {
        foreach (var item in BlobAgentCases.All())
        {
            var descriptor = BlobAgentCatalogTests.Find(item.Name);
            var arguments = item.Arguments();
            arguments[BlobAgentCases.AuthorityMember] = JsonSerializer.SerializeToElement(BlobAgentCases.Principal);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => descriptor.Decode(arguments)).Code).IsEqualTo(ErrorCode.Validation);
            arguments.Remove(BlobAgentCases.AuthorityMember);
            var node = JsonNode.Parse(item.Request.GetRawText())!.AsObject();
            node[BlobAgentCases.AuthorityMember] = BlobAgentCases.Principal;
            arguments[BlobAgentCases.Request] = JsonSerializer.SerializeToElement(node);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => descriptor.Decode(arguments)).Code).IsEqualTo(ErrorCode.Validation);
            arguments[BlobAgentCases.Request] = JsonSerializer.SerializeToElement<object?>(null);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => descriptor.Decode(arguments)).Code).IsEqualTo(ErrorCode.Validation);
        }
    }

    /// <summary>Required canonical bytes reject null and malformed base64 before dispatch.</summary>
    [Test]
    public async Task AcBlob007PartBytesRejectNullAndMalformedBase64()
    {
        var item = BlobAgentCases.All().Single(item => item.Name == BlobAgentCases.Part);
        foreach (var bytes in new string?[] { null, InvalidBytes })
        {
            var node = JsonNode.Parse(item.Request.GetRawText())!.AsObject();
            node[BlobAgentCases.BytesMember] = bytes is null ? null : JsonValue.Create(bytes);
            var arguments = item.Arguments();
            arguments[BlobAgentCases.Request] = JsonSerializer.SerializeToElement(node);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => BlobAgentCatalogTests.Find(item.Name).Decode(arguments)).Code)
                .IsEqualTo(ErrorCode.Validation);
        }
    }
}
