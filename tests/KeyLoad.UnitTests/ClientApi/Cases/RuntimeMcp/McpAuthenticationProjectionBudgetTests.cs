using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class McpAuthenticationProjectionBudgetTests
{
    private const int RequestCapacity = 16_384;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DecodedPrincipalHonorsExactProjectionLimitsAndReleasesIngress(bool properties)
    {
        using var database = new TestDatabase();
        var principal = McpAuthenticationTests.Principal(database) with
        {
            Grants = [new(McpAuthenticationTests.SharedValue, McpAuthenticationTests.SharedValue, Capability.All)],
            FieldGrants = [McpAuthenticationTests.SharedValue],
            Projects = [McpAuthenticationTests.SharedValue],
            Teams = [McpAuthenticationTests.SharedValue]
        };
        var payload = OrleansSerialization.Serialize(new GrainValue(principal));
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
        var limits = new McpMemoryLimits();
        var memory = new McpMemoryBudget(UnitMcpOptions.Memory(limits));
        var governor = new HttpAdmissionGovernor(UnitAdmissionOptions.Http());
        using (var refused = new McpRequestState(governor, memory, RequestCapacity, limited, CancellationToken.None))
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
                refused.Authenticate(payload, CancellationToken.None)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
            await Assert.That(refused.Principal).IsNull();
        }
        McpAuthenticationTests.AssertFullPools(memory, limits);
        using (var healthy = new McpRequestState(governor, memory, RequestCapacity, exact, CancellationToken.None))
        {
            healthy.Authenticate(payload, CancellationToken.None);
            await Assert.That(JsonDefaults.Serialize(healthy.Principal).AsSpan().SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
        }
        McpAuthenticationTests.AssertFullPools(memory, limits);
        await Assert.That(governor.Status().Node.ControlCommands).IsEqualTo(0);
    }
}
