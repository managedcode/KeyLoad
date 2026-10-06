using System.Reflection;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class McpNativeAuthenticationMalformedReaderTests
{
    [Test]
    public async Task EveryNativeAuthenticationPrefixIsValidationAndACompleteTwinStillDecodes()
    {
        using var database = new TestDatabase();
        var principal = McpNativeAuthenticationTests.Principal(database);
        var payload = McpAuthenticationMalformedFixture.Valid(principal);
        for (var length = 0; length < payload.Length; length++)
        {
            var prefix = payload[..length];
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpNativeAuthentication.Inspect(prefix, CancellationToken.None, UnitMcpOptions.Execution())).Code)
                .IsEqualTo(ErrorCode.Validation);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpNativeAuthentication.ReadPrincipal(prefix, CancellationToken.None, UnitMcpOptions.Execution())).Code)
                .IsEqualTo(ErrorCode.Validation);
        }
        var decoded = McpNativeAuthentication.ReadPrincipal(payload, CancellationToken.None, UnitMcpOptions.Execution());
        await Assert.That(JsonDefaults.Serialize(decoded).AsSpan().SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
    }

    [Test]
    public async Task OwnedInvalidOperationIncludingTheSameBufferMessageIsOutsideMalformedFilter()
    {
        var filter = typeof(McpNativeAuthentication).GetMethod("Malformed", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var message in new[] { "Owned session invariant failed.", "Insufficient data present in buffer." })
        {
            var failure = Assert.ThrowsExactly<InvalidOperationException>(() => throw new InvalidOperationException(message));
            await Assert.That((bool)filter.Invoke(null, [failure])!).IsFalse();
        }
    }
}
