using System.Text;
using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class McpNativeAuthenticationMalformedTests
{
    private const string Utf8Marker = "native-auth-utf8-marker";
    private const byte InvalidUtf8 = 0xff;
    private const byte TrailingByte = 0;
    private const int RequestCapacity = 16_384;
    private const string WrongValue = "a principal reply cannot be a string";
    private const string MarkerMissing = "The genuine native string marker was not encoded.";

    [Test]
    [Arguments(McpAuthenticationArrayFault.TooManyItems, ErrorCode.ResourceExhausted)]
    [Arguments(McpAuthenticationArrayFault.TooManyGrantProperties, ErrorCode.ResourceExhausted)]
    [Arguments(McpAuthenticationArrayFault.Underfilled, ErrorCode.Validation)]
    [Arguments(McpAuthenticationArrayFault.Overfilled, ErrorCode.Validation)]
    [Arguments(McpAuthenticationArrayFault.WrongType, ErrorCode.Validation)]
    [Arguments(McpAuthenticationArrayFault.WrongReferenceType, ErrorCode.Validation)]
    [Arguments(McpAuthenticationArrayFault.NullElement, ErrorCode.Validation)]
    public async Task MalformedOrOverBudgetNativeCollectionsFailBeforeTypedPrincipalAllocation(McpAuthenticationArrayFault fault, ErrorCode expected)
    {
        using var database = new TestDatabase();
        var principal = McpNativeAuthenticationTests.Principal(database);
        var twin = McpAuthenticationMalformedFixture.Valid(principal);
        var expectedShape = McpFrameBounds.InspectValue(JsonDefaults.Serialize(principal), McpFramingProtocol.MaximumDataReplyBytes);
        await Assert.That(McpNativeAuthentication.Inspect(twin, CancellationToken.None)).IsEqualTo(expectedShape);
        var decoded = McpNativeAuthentication.ReadPrincipal(twin, CancellationToken.None);
        await Assert.That(JsonDefaults.Serialize(decoded).AsSpan().SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
        var payload = McpAuthenticationMalformedFixture.Array(principal, fault);
        await Assert.That(payload.AsSpan().SequenceEqual(twin)).IsFalse();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpNativeAuthentication.Inspect(payload, CancellationToken.None)).Code)
            .IsEqualTo(expected);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpNativeAuthentication.ReadPrincipal(payload, CancellationToken.None)).Code)
            .IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    public async Task WrongNativeRootsDefaultCollectionsAndTrailingBytesReleaseEveryIngressReservation()
    {
        using var database = new TestDatabase();
        var principal = McpNativeAuthenticationTests.Principal(database);
        var valid = NativeSerialization.Serialize(new GrainValue(principal));
        byte[][] malformed =
        [
            NativeSerialization.Serialize(principal), NativeSerialization.Serialize(new GrainValue(WrongValue)),
            NativeSerialization.Serialize(new GrainValue(null)), McpAuthenticationMalformedFixture.DefaultArray(principal),
            [.. valid, TrailingByte], valid[..^1]
        ];
        var limits = new McpMemoryLimits();
        var memory = new McpMemoryBudget(limits.DataBytes, limits.ControlBytes, limits.IngressBytes);
        foreach (var payload in malformed)
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpNativeAuthentication.ReadPrincipal(payload, CancellationToken.None)).Code)
                .IsEqualTo(ErrorCode.Validation);
            using (var state = new McpRequestState(new HttpAdmissionGovernor(UnitAdmissionOptions.Http()), memory, RequestCapacity, CancellationToken.None))
            {
                await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => state.Authenticate(payload, CancellationToken.None)).Code)
                    .IsEqualTo(ErrorCode.Validation);
            }
            McpNativeAuthenticationTests.AssertFullPools(memory, limits);
        }
    }

    [Test]
    public async Task InvalidNativeStringUtf8IsRejectedBeforeReplacementDecoderCanCreatePrincipal()
    {
        using var database = new TestDatabase();
        var principal = McpNativeAuthenticationTests.Principal(database) with { Id = Utf8Marker };
        var payload = NativeSerialization.Serialize(new GrainValue(principal));
        var offset = payload.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Utf8Marker));
        if (offset < 0)
        { throw new InvalidOperationException(MarkerMissing); }
        payload[offset] = InvalidUtf8;
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpNativeAuthentication.Inspect(payload, CancellationToken.None)).Code)
            .IsEqualTo(ErrorCode.Validation);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpNativeAuthentication.ReadPrincipal(payload, CancellationToken.None)).Code)
            .IsEqualTo(ErrorCode.Validation);
    }
}
