using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-004/005: successful lease growth and disposal preserve overflow-safe lane accounting.</summary>
internal sealed class McpMemoryBudgetGrowthTests
{
    private const long DataCapacity = 10;
    private const long ControlCapacity = 1;
    private const long IngressCapacity = 1;

    /// <summary>Growing a partial lease to capacity and disposing it releases the entire grown reservation once.</summary>
    [Test]
    public async Task DisposeAfterGrowthReleasesFullReservation()
    {
        var budget = new McpMemoryBudget(Microsoft.Extensions.Options.Options.Create(new KeyLoad.Server.McpMemoryLimits { DataBytes = DataCapacity, ControlBytes = ControlCapacity, IngressBytes = IngressCapacity }));
        var lease = budget.Reserve(McpMemoryLane.Data, 6);
        lease.GrowTo(DataCapacity);
        lease.Dispose();

        using var full = budget.Reserve(McpMemoryLane.Data, DataCapacity);
        var exhausted = Assert.ThrowsExactly<KeyLoadException>(() => budget.Reserve(McpMemoryLane.Data, 1));
        await Assert.That(exhausted.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>A small lease can grow directly to the largest positive byte count without overflowing accounting.</summary>
    [Test]
    public async Task GrowthToMaximumLongDoesNotOverflow()
    {
        var budget = new McpMemoryBudget(Microsoft.Extensions.Options.Options.Create(new KeyLoad.Server.McpMemoryLimits { DataBytes = long.MaxValue, ControlBytes = ControlCapacity, IngressBytes = IngressCapacity }));
        using var lease = budget.Reserve(McpMemoryLane.Data, 1);
        lease.GrowTo(long.MaxValue);

        var exhausted = Assert.ThrowsExactly<KeyLoadException>(() => budget.Reserve(McpMemoryLane.Data, 1));
        await Assert.That(exhausted.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }
}
