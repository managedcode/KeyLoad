using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-004: independent configured pools reject invalid limits before hosting.</summary>
internal sealed class McpMemoryLimitsTests
{
    /// <summary>Defaults provide distinct data, control and ingress capacities.</summary>
    [Test]
    public async Task DefaultsHaveIndependentPositiveCapacities()
    {
        var limits = new McpMemoryLimits();
        limits.Validate();
        await Assert.That(limits.DataBytes).IsEqualTo(2_147_483_648L);
        await Assert.That(limits.ControlBytes).IsEqualTo(134_217_728L);
        await Assert.That(limits.IngressBytes).IsEqualTo(1_073_741_824L);
    }

    /// <summary>Every configured pool must be positive independently.</summary>
    /// <param name="capacity">An invalid byte capacity.</param>
    [Test]
    [Arguments(0L)]
    [Arguments(-1L)]
    public void InvalidCapacityRejectsEachPool(long capacity)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => (new McpMemoryLimits { DataBytes = capacity }).Validate());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => (new McpMemoryLimits { ControlBytes = capacity }).Validate());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => (new McpMemoryLimits { IngressBytes = capacity }).Validate());
    }

    /// <summary>Explicit small positive capacities remain accepted; admission enforces sufficiency.</summary>
    [Test]
    public async Task PositiveCapacitiesArePreservedWithoutImplicitClamping()
    {
        var limits = new McpMemoryLimits { DataBytes = 1, ControlBytes = 2, IngressBytes = 3 };
        limits.Validate();
        await Assert.That(limits.DataBytes).IsEqualTo(1L);
        await Assert.That(limits.ControlBytes).IsEqualTo(2L);
        await Assert.That(limits.IngressBytes).IsEqualTo(3L);
    }
}
