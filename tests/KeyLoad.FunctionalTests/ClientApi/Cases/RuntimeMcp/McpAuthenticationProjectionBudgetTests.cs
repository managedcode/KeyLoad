using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.ClientApi;

internal sealed class McpAuthenticationProjectionBudgetTests
{
    [Test]
    public async Task TypedPrincipalHonorsExactByteLimitBeforeIdentityAdmission()
    {
        using var database = new TestDatabase();
        var principal = McpAuthenticationTests.Principal(database);
        var payload = new GrainValue(principal);
        var bytes = OrleansSerialization.Serialize(payload).Length;
        var exact = UnitMcpOptions.Execution(new() { MaximumAuthenticationBytes = bytes });
        var limited = UnitMcpOptions.Execution(new() { MaximumAuthenticationBytes = bytes - 1 });
        using var refused = new McpRequestState(McpFramingProtocol.MaximumDataReplyBytes, limited);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            refused.Authenticate(payload, CancellationToken.None)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(refused.Principal).IsNull();
        using var healthy = new McpRequestState(McpFramingProtocol.MaximumDataReplyBytes, exact);
        healthy.Authenticate(payload, CancellationToken.None);
        await Assert.That(JsonDefaults.Serialize(healthy.Principal).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DecodedPrincipalHonorsExactProjectionLimits(bool properties)
    {
        using var database = new TestDatabase();
        var principal = McpAuthenticationTests.Principal(database) with
        {
            Grants = [new(McpAuthenticationTests.SharedValue, McpAuthenticationTests.SharedValue, Capability.All)],
            FieldGrants = [McpAuthenticationTests.SharedValue],
            Projects = [McpAuthenticationTests.SharedValue],
            Teams = [McpAuthenticationTests.SharedValue]
        };
        var payload = new GrainValue(principal);
        var shape = McpFrameBounds.InspectValue(JsonDefaults.Serialize(principal),
            McpFramingProtocol.MaximumDataReplyBytes, UnitMcpOptions.Execution());
        var exact = UnitMcpOptions.Execution(new()
        {
            MaximumTokens = shape.TokenCount,
            MaximumProperties = shape.PropertyCount
        });
        var limited = UnitMcpOptions.Execution(new()
        {
            MaximumTokens = shape.TokenCount - (properties ? 0 : 1),
            MaximumProperties = shape.PropertyCount - (properties ? 1 : 0)
        });
        await Assert.That(McpAuthentication.Inspect(payload, exact, CancellationToken.None)).IsEqualTo(shape);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            McpAuthentication.Inspect(payload, limited, CancellationToken.None)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        using (var refused = new McpRequestState(McpFramingProtocol.MaximumDataReplyBytes, limited))
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
                refused.Authenticate(payload, CancellationToken.None)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
            await Assert.That(refused.Principal).IsNull();
        }
        using var healthy = new McpRequestState(McpFramingProtocol.MaximumDataReplyBytes, exact);
        healthy.Authenticate(payload, CancellationToken.None);
        await Assert.That(JsonDefaults.Serialize(healthy.Principal).AsSpan().SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
    }
}
