using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClientApi;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class UnknownWellKnownHeaderAuthenticationTests
{
    [Test]
    [Arguments(UnknownWellKnownHeaderAuthenticationScope.Envelope)]
    [Arguments(UnknownWellKnownHeaderAuthenticationScope.Value)]
    [Arguments(UnknownWellKnownHeaderAuthenticationScope.Member)]
    [Arguments(UnknownWellKnownHeaderAuthenticationScope.ArrayCount)]
    public async Task AcR17002UnknownAuthMetadataKeepsValidationAndTheSameWriterTwinPreservesThePrincipal(
        UnknownWellKnownHeaderAuthenticationScope scope)
    {
        using var database = new TestDatabase();
        var expected = McpAuthenticationTests.Principal(database);
        var source = McpAuthenticationMalformedFixture.Valid(expected);
        var valid = UnknownWellKnownHeaderAuthenticationFixture.Encode(source, scope, malformed: false);
        var invalid = UnknownWellKnownHeaderAuthenticationFixture.Encode(source, scope, malformed: true);
        await Assert.That(valid.AsSpan().SequenceEqual(source)).IsTrue();
        await Assert.That(valid.AsSpan().SequenceEqual(invalid)).IsFalse();
        var shape = McpAuthentication.Inspect(valid, UnitMcpOptions.Execution(), CancellationToken.None);
        var actual = McpAuthentication.ReadPrincipal(valid, UnitMcpOptions.Execution(), CancellationToken.None);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpAuthentication.Inspect(invalid, UnitMcpOptions.Execution(), CancellationToken.None)).Code)
            .IsEqualTo(ErrorCode.Validation);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpAuthentication.ReadPrincipal(invalid, UnitMcpOptions.Execution(), CancellationToken.None)).Code)
            .IsEqualTo(ErrorCode.Validation);
        await Assert.That(McpAuthentication.Inspect(valid, UnitMcpOptions.Execution(), CancellationToken.None)).IsEqualTo(shape);
        await Assert.That(JsonDefaults.Serialize(McpAuthentication.ReadPrincipal(valid, UnitMcpOptions.Execution(), CancellationToken.None)).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
