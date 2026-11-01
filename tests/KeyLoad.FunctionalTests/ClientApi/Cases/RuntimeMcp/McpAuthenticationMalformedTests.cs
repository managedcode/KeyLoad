using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.ClientApi;

internal sealed class McpAuthenticationMalformedTests
{
    private const string WrongValue = "a principal reply cannot be a string";

    [Test]
    public async Task WrongValuesAreRefusedBeforePrincipalAdmission()
    {
        using var database = new TestDatabase();
        var principal = McpAuthenticationTests.Principal(database);
        GrainValue[] invalid = [new(WrongValue), new(null)];
        foreach (var result in invalid)
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
                McpAuthentication.ReadPrincipal(result, UnitMcpOptions.Execution(), CancellationToken.None)).Code)
                .IsEqualTo(ErrorCode.Validation);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
                McpAuthentication.Inspect(result, UnitMcpOptions.Execution(), CancellationToken.None)).Code)
                .IsEqualTo(ErrorCode.Validation);
            using var refused = new McpRequestState(McpFramingProtocol.MaximumDataReplyBytes, UnitMcpOptions.Execution());
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
                refused.Authenticate(result, CancellationToken.None)).Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(refused.Principal).IsNull();
        }

        using var healthy = new McpRequestState(McpFramingProtocol.MaximumDataReplyBytes, UnitMcpOptions.Execution());
        healthy.Authenticate(new(principal), CancellationToken.None);
        await Assert.That(JsonDefaults.Serialize(healthy.Principal).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
    }
}
