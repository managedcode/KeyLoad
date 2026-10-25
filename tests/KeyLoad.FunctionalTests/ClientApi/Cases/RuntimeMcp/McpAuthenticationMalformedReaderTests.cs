using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.ClientApi;

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
}
