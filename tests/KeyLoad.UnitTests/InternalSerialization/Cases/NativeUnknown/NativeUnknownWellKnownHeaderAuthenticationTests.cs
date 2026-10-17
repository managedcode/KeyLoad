using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClientApi;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeUnknownWellKnownHeaderAuthenticationTests
{
    [Test]
    [Arguments(NativeUnknownWellKnownHeaderAuthenticationScope.Envelope)]
    [Arguments(NativeUnknownWellKnownHeaderAuthenticationScope.Value)]
    [Arguments(NativeUnknownWellKnownHeaderAuthenticationScope.Member)]
    [Arguments(NativeUnknownWellKnownHeaderAuthenticationScope.ArrayCount)]
    public async Task AcR17002UnknownAuthMetadataKeepsValidationAndTheSameWriterTwinPreservesThePrincipal(
        NativeUnknownWellKnownHeaderAuthenticationScope scope)
    {
        using var database = new TestDatabase();
        var expected = McpNativeAuthenticationTests.Principal(database);
        var source = McpAuthenticationMalformedFixture.Valid(expected);
        var valid = NativeUnknownWellKnownHeaderAuthenticationFixture.Encode(source, scope, malformed: false);
        var invalid = NativeUnknownWellKnownHeaderAuthenticationFixture.Encode(source, scope, malformed: true);
        await Assert.That(valid.AsSpan().SequenceEqual(source)).IsTrue();
        await Assert.That(valid.AsSpan().SequenceEqual(invalid)).IsFalse();
        var shape = McpNativeAuthentication.Inspect(valid, UnitMcpOptions.Execution(), CancellationToken.None);
        var actual = McpNativeAuthentication.ReadPrincipal(valid, UnitMcpOptions.Execution(), CancellationToken.None);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpNativeAuthentication.Inspect(invalid, UnitMcpOptions.Execution(), CancellationToken.None)).Code)
            .IsEqualTo(ErrorCode.Validation);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpNativeAuthentication.ReadPrincipal(invalid, UnitMcpOptions.Execution(), CancellationToken.None)).Code)
            .IsEqualTo(ErrorCode.Validation);
        await Assert.That(McpNativeAuthentication.Inspect(valid, UnitMcpOptions.Execution(), CancellationToken.None)).IsEqualTo(shape);
        await Assert.That(JsonDefaults.Serialize(McpNativeAuthentication.ReadPrincipal(valid, UnitMcpOptions.Execution(), CancellationToken.None)).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
