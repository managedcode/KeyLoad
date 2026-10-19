using System.Text;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class McpAuthenticationMalformedTests
{
    private const string Utf8Marker = "execution-auth-utf8-marker";
    private const byte InvalidUtf8 = 0xff;
    private const byte TrailingByte = 0;
    private const int RequestCapacity = 16_384;
    private const string WrongValue = "a principal reply cannot be a string";
    private const string MarkerMissing = "The genuine execution string marker was not encoded.";

    [Test]
    [Arguments(McpAuthenticationArrayFault.TooManyItems, ErrorCode.ResourceExhausted)]
    [Arguments(McpAuthenticationArrayFault.TooManyGrantProperties, ErrorCode.ResourceExhausted)]
    [Arguments(McpAuthenticationArrayFault.Underfilled, ErrorCode.Validation)]
    [Arguments(McpAuthenticationArrayFault.Overfilled, ErrorCode.Validation)]
    [Arguments(McpAuthenticationArrayFault.WrongType, ErrorCode.Validation)]
    [Arguments(McpAuthenticationArrayFault.WrongReferenceType, ErrorCode.Validation)]
    [Arguments(McpAuthenticationArrayFault.NullElement, ErrorCode.Validation)]
    public async Task MalformedOrOverBudgetCollectionsFailBeforeTypedPrincipalAllocation(McpAuthenticationArrayFault fault, ErrorCode expected)
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
            .IsEqualTo(expected);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpAuthentication.ReadPrincipal(payload, UnitMcpOptions.Execution(), CancellationToken.None)).Code)
            .IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    public async Task WrongRootsDefaultCollectionsAndTrailingBytesReleaseEveryIngressReservation()
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
        var limits = new McpMemoryLimits();
        var memory = new McpMemoryBudget(Microsoft.Extensions.Options.Options.Create(new KeyLoad.Server.McpMemoryLimits { DataBytes = limits.DataBytes, ControlBytes = limits.ControlBytes, IngressBytes = limits.IngressBytes }));
        foreach (var payload in malformed)
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpAuthentication.ReadPrincipal(payload, UnitMcpOptions.Execution(), CancellationToken.None)).Code)
                .IsEqualTo(ErrorCode.Validation);
            using (var state = new McpRequestState(new HttpAdmissionGovernor(UnitAdmissionOptions.Http()), memory, RequestCapacity, UnitMcpOptions.Execution(), CancellationToken.None))
            {
                await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => state.Authenticate(payload, CancellationToken.None)).Code)
                    .IsEqualTo(ErrorCode.Validation);
            }
            McpAuthenticationTests.AssertFullPools(memory, limits);
        }
    }

    [Test]
    public async Task InvalidStringUtf8IsRejectedBeforeReplacementDecoderCanCreatePrincipal()
    {
        using var database = new TestDatabase();
        var principal = McpAuthenticationTests.Principal(database) with
        {
            Id = Utf8Marker
        };
        var payload = OrleansSerialization.Serialize(new GrainValue(principal));
        var offset = payload.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Utf8Marker));
        if (offset < 0)
        {
            throw new InvalidOperationException(MarkerMissing);
        }
        payload[offset] = InvalidUtf8;
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpAuthentication.Inspect(payload, UnitMcpOptions.Execution(), CancellationToken.None)).Code)
            .IsEqualTo(ErrorCode.Validation);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpAuthentication.ReadPrincipal(payload, UnitMcpOptions.Execution(), CancellationToken.None)).Code)
            .IsEqualTo(ErrorCode.Validation);
    }
}
