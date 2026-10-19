using System.Reflection;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class McpAuthenticationMalformedReaderTests
{
    [Test]
    public async Task EveryAuthenticationPrefixIsValidationAndACompleteTwinStillDecodes()
    {
        using var database = new TestDatabase();
        var principal = McpAuthenticationTests.Principal(database);
        var payload = McpAuthenticationMalformedFixture.Valid(principal);
        for (var length = 0; length < payload.Length; length++)
        {
            var prefix = payload[..length];
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpAuthentication.Inspect(prefix, UnitMcpOptions.Execution(), CancellationToken.None)).Code)
                .IsEqualTo(ErrorCode.Validation);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => McpAuthentication.ReadPrincipal(prefix, UnitMcpOptions.Execution(), CancellationToken.None)).Code)
                .IsEqualTo(ErrorCode.Validation);
        }
        var decoded = McpAuthentication.ReadPrincipal(payload, UnitMcpOptions.Execution(), CancellationToken.None);
        await Assert.That(JsonDefaults.Serialize(decoded).AsSpan().SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
    }

    [Test]
    public async Task OwnedInvalidOperationIncludingTheSameBufferMessageIsOutsideMalformedFilter()
    {
        var filter = typeof(McpAuthentication).GetMethod("Malformed", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var message in new[] { "Owned session invariant failed.", "Insufficient data present in buffer." })
        {
            var failure = Assert.ThrowsExactly<InvalidOperationException>(() => throw new InvalidOperationException(message));
            await Assert.That((bool)filter.Invoke(null, [failure])!).IsFalse();
        }
    }
}
