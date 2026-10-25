using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.ClientApi;

internal sealed class McpAuthenticationMalformedTests
{
    private const byte TrailingByte = 0;
    private const string WrongValue = "a principal reply cannot be a string";

    [Test]
    [Arguments(McpAuthenticationArrayFault.Underfilled)]
    [Arguments(McpAuthenticationArrayFault.Overfilled)]
    [Arguments(McpAuthenticationArrayFault.WrongType)]
    [Arguments(McpAuthenticationArrayFault.WrongReferenceType)]
    [Arguments(McpAuthenticationArrayFault.NullElement)]
    public async Task MalformedCollectionsRejectAndHealthyPrincipalStillDecodes(McpAuthenticationArrayFault fault)
    {
        using var database = new TestDatabase();
        var principal = McpAuthenticationTests.Principal(database);
        var twin = McpAuthenticationMalformedFixture.Valid(principal);
        var expectedShape = McpFrameBounds.InspectValue(JsonDefaults.Serialize(principal), McpFramingProtocol.MaximumDataReplyBytes, UnitMcpOptions.Execution());
        await Assert.That(McpAuthentication.Inspect(twin, UnitMcpOptions.Execution(), CancellationToken.None)).IsEqualTo(expectedShape);
        var decoded = McpAuthentication.ReadPrincipal(twin, UnitMcpOptions.Execution(), CancellationToken.None);
        await Assert.That(JsonDefaults.Serialize(decoded).AsSpan().SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
        var payload = McpAuthenticationMalformedFixture.Array(principal, fault);
        await Assert.That(payload.AsSpan().SequenceEqual(twin)).IsFalse();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpAuthentication.Inspect(payload, UnitMcpOptions.Execution(), CancellationToken.None)).Code)
            .IsEqualTo(ErrorCode.Validation);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpAuthentication.ReadPrincipal(payload, UnitMcpOptions.Execution(), CancellationToken.None)).Code)
            .IsEqualTo(ErrorCode.Validation);
        var healthy = McpAuthentication.ReadPrincipal(twin, UnitMcpOptions.Execution(), CancellationToken.None);
        await Assert.That(JsonDefaults.Serialize(healthy).AsSpan().SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
    }

    [Test]
    public async Task WrongRootsDefaultCollectionsAndTrailingBytesAreRejected()
    {
        using var database = new TestDatabase();
        var principal = McpAuthenticationTests.Principal(database);
        var valid = OrleansSerialization.Serialize(new GrainValue(principal));
        byte[][] malformed =
        [
            OrleansSerialization.Serialize(principal), OrleansSerialization.Serialize(new GrainValue(WrongValue)),
            OrleansSerialization.Serialize(new GrainValue(null)), McpAuthenticationMalformedFixture.DefaultArray(principal),
            McpAuthenticationMalformedFixture.DefaultTeams(principal),
            [.. valid, TrailingByte], valid[..^1]
        ];
        foreach (var payload in malformed)
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpAuthentication.ReadPrincipal(payload, UnitMcpOptions.Execution(), CancellationToken.None)).Code)
                .IsEqualTo(ErrorCode.Validation);
            using var state = new McpRequestState(McpFramingProtocol.MaximumDataReplyBytes, UnitMcpOptions.Execution());
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => state.Authenticate(payload, CancellationToken.None)).Code)
                .IsEqualTo(ErrorCode.Validation);
        }
    }
}
