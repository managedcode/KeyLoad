using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-004/005: byte leases isolate memory lanes and preserve held capacity across failed growth.</summary>
internal sealed class McpMemoryBudgetTests
{
    private const long DataCapacity = 10;
    private const long ControlCapacity = 4;
    private const long IngressCapacity = 3;

    /// <summary>Each lane must have a positive independently configured capacity.</summary>
    /// <param name="dataBytes">The candidate data-lane capacity.</param>
    /// <param name="controlBytes">The candidate control-lane capacity.</param>
    /// <param name="ingressBytes">The candidate ingress-lane capacity.</param>
    /// <param name="expectedParameterName">The constructor argument expected to be rejected.</param>
    [Test]
    [Arguments(-1L, 1L, 1L, "dataBytes")]
    [Arguments(1L, -1L, 1L, "controlBytes")]
    [Arguments(1L, 1L, -1L, "ingressBytes")]
    [Arguments(0L, 1L, 1L, "dataBytes")]
    [Arguments(1L, 0L, 1L, "controlBytes")]
    [Arguments(1L, 1L, 0L, "ingressBytes")]
    public async Task NonPositiveLaneCapacityIsRejectedWithItsParameterName(
        long dataBytes,
        long controlBytes,
        long ingressBytes,
        string expectedParameterName)
    {
        var error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new McpMemoryBudget(Microsoft.Extensions.Options.Options.Create(new KeyLoad.Server.McpMemoryLimits { DataBytes = dataBytes, ControlBytes = controlBytes, IngressBytes = ingressBytes })));
        await Assert.That(error.ParamName).IsEqualTo(expectedParameterName);
    }

    /// <summary>Reservations in one lane cannot consume or release another lane's capacity.</summary>
    [Test]
    public async Task LanesRemainIndependentAtTheirCapacityBoundaries()
    {
        var budget = new McpMemoryBudget(Microsoft.Extensions.Options.Options.Create(new KeyLoad.Server.McpMemoryLimits { DataBytes = DataCapacity, ControlBytes = ControlCapacity, IngressBytes = IngressCapacity }));
        using var data = budget.Reserve(McpMemoryLane.Data, DataCapacity);
        using var control = budget.Reserve(McpMemoryLane.Control, ControlCapacity);
        using var ingress = budget.Reserve(McpMemoryLane.Ingress, IngressCapacity);

        foreach (var lane in Enum.GetValues<McpMemoryLane>())
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => budget.Reserve(lane, 1));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        }
    }

    /// <summary>Zero-byte reservations are valid and do not consume lane capacity.</summary>
    [Test]
    public async Task ZeroByteLeaseDoesNotConsumeCapacity()
    {
        var budget = new McpMemoryBudget(Microsoft.Extensions.Options.Options.Create(new KeyLoad.Server.McpMemoryLimits { DataBytes = DataCapacity, ControlBytes = ControlCapacity, IngressBytes = IngressCapacity }));
        using var empty = budget.Reserve(McpMemoryLane.Data, 0);
        empty.GrowTo(0);
        using var full = budget.Reserve(McpMemoryLane.Data, DataCapacity);

        var exhausted = Assert.ThrowsExactly<KeyLoadException>(() => budget.Reserve(McpMemoryLane.Data, 1));
        await Assert.That(exhausted.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>Near-maximum byte accounting fails safely instead of wrapping when capacity is exhausted.</summary>
    [Test]
    public async Task MaximumLongCapacityDoesNotOverflowOnAdditionalReservation()
    {
        var budget = new McpMemoryBudget(Microsoft.Extensions.Options.Options.Create(new KeyLoad.Server.McpMemoryLimits { DataBytes = long.MaxValue, ControlBytes = ControlCapacity, IngressBytes = IngressCapacity }));
        using var full = budget.Reserve(McpMemoryLane.Data, long.MaxValue);

        var exhausted = Assert.ThrowsExactly<KeyLoadException>(() => budget.Reserve(McpMemoryLane.Data, 1));
        await Assert.That(exhausted.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>Negative reservation and growth sizes fail explicitly without changing held capacity.</summary>
    [Test]
    public async Task NegativeReservationAndGrowthSizesAreRejected()
    {
        var budget = new McpMemoryBudget(Microsoft.Extensions.Options.Options.Create(new KeyLoad.Server.McpMemoryLimits { DataBytes = DataCapacity, ControlBytes = ControlCapacity, IngressBytes = IngressCapacity }));
        var reserveError = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => budget.Reserve(McpMemoryLane.Data, -1));
        await Assert.That(reserveError.ParamName).IsEqualTo("retainedBytes");
        using var lease = budget.Reserve(McpMemoryLane.Data, 6);
        var growthError = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => lease.GrowTo(-1));
        await Assert.That(growthError.ParamName).IsEqualTo("totalBytes");

        using var remainder = budget.Reserve(McpMemoryLane.Data, 4);
        var exhausted = Assert.ThrowsExactly<KeyLoadException>(() => budget.Reserve(McpMemoryLane.Data, 1));
        await Assert.That(exhausted.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>An exhausted growth leaves the original reservation intact and available after another lease releases.</summary>
    [Test]
    public async Task FailedGrowthRetainsOriginalReservation()
    {
        var budget = new McpMemoryBudget(Microsoft.Extensions.Options.Options.Create(new KeyLoad.Server.McpMemoryLimits { DataBytes = DataCapacity, ControlBytes = ControlCapacity, IngressBytes = IngressCapacity }));
        using var original = budget.Reserve(McpMemoryLane.Data, 6);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => original.GrowTo(DataCapacity + 1));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);

        using (budget.Reserve(McpMemoryLane.Data, 4))
        {
            var stillExhausted = Assert.ThrowsExactly<KeyLoadException>(() => budget.Reserve(McpMemoryLane.Data, 1));
            await Assert.That(stillExhausted.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        }

        original.GrowTo(DataCapacity);
        var exhaustedAfterGrow = Assert.ThrowsExactly<KeyLoadException>(() => budget.Reserve(McpMemoryLane.Data, 1));
        await Assert.That(exhaustedAfterGrow.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>A cancelled growth leaves the current lease and its unreserved remainder unchanged.</summary>
    [Test]
    public async Task CancelledGrowthRetainsOriginalReservation()
    {
        var budget = new McpMemoryBudget(Microsoft.Extensions.Options.Options.Create(new KeyLoad.Server.McpMemoryLimits { DataBytes = DataCapacity, ControlBytes = ControlCapacity, IngressBytes = IngressCapacity }));
        using var lease = budget.Reserve(McpMemoryLane.Data, 6);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Assert.ThrowsExactly<OperationCanceledException>(() => lease.GrowTo(DataCapacity, cancelled.Token));
        using var remainder = budget.Reserve(McpMemoryLane.Data, 4);
        var exhausted = Assert.ThrowsExactly<KeyLoadException>(() => budget.Reserve(McpMemoryLane.Data, 1));
        await Assert.That(exhausted.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>A cancelled reservation does not spend lane capacity.</summary>
    [Test]
    public async Task CancelledReservationDoesNotConsumeCapacity()
    {
        var budget = new McpMemoryBudget(Microsoft.Extensions.Options.Options.Create(new KeyLoad.Server.McpMemoryLimits { DataBytes = DataCapacity, ControlBytes = ControlCapacity, IngressBytes = IngressCapacity }));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Assert.ThrowsExactly<OperationCanceledException>(() => budget.Reserve(McpMemoryLane.Data, DataCapacity, cancelled.Token));
        using var full = budget.Reserve(McpMemoryLane.Data, DataCapacity);
        var exhausted = Assert.ThrowsExactly<KeyLoadException>(() => budget.Reserve(McpMemoryLane.Data, 1));
        await Assert.That(exhausted.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>Undefined lane values are rejected before any lane capacity can be reserved.</summary>
    [Test]
    public async Task UndefinedLaneIsRejected()
    {
        var budget = new McpMemoryBudget(Microsoft.Extensions.Options.Options.Create(new KeyLoad.Server.McpMemoryLimits { DataBytes = DataCapacity, ControlBytes = ControlCapacity, IngressBytes = IngressCapacity }));
        var error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            budget.Reserve((McpMemoryLane)int.MaxValue, 1));
        await Assert.That(error.ParamName).IsEqualTo("lane");
    }

    /// <summary>Shrinking is rejected and does not reduce the existing lease.</summary>
    [Test]
    public async Task ShrinkingLeaseIsRejectedWithoutReleasingCapacity()
    {
        var budget = new McpMemoryBudget(Microsoft.Extensions.Options.Options.Create(new KeyLoad.Server.McpMemoryLimits { DataBytes = DataCapacity, ControlBytes = ControlCapacity, IngressBytes = IngressCapacity }));
        using var lease = budget.Reserve(McpMemoryLane.Data, 6);
        Assert.ThrowsExactly<InvalidOperationException>(() => lease.GrowTo(5));

        using var remainder = budget.Reserve(McpMemoryLane.Data, 4);
        var exhausted = Assert.ThrowsExactly<KeyLoadException>(() => budget.Reserve(McpMemoryLane.Data, 1));
        await Assert.That(exhausted.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>Dispose is idempotent, releases capacity once, and makes later growth fail as disposed.</summary>
    [Test]
    public async Task DisposeIsIdempotentAndMakesLeaseUnavailable()
    {
        var budget = new McpMemoryBudget(Microsoft.Extensions.Options.Options.Create(new KeyLoad.Server.McpMemoryLimits { DataBytes = DataCapacity, ControlBytes = ControlCapacity, IngressBytes = IngressCapacity }));
        var lease = budget.Reserve(McpMemoryLane.Data, DataCapacity);
        lease.Dispose();
        lease.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => lease.GrowTo(DataCapacity));

        using var reclaimed = budget.Reserve(McpMemoryLane.Data, DataCapacity);
        var exhausted = Assert.ThrowsExactly<KeyLoadException>(() => budget.Reserve(McpMemoryLane.Data, 1));
        await Assert.That(exhausted.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }
}

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
