using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class ChunkMultiblockReadBudgetAssertions
{
    private const long OneByte = 1;

    internal static async Task RequireAsync(ChunkSelectedIntegrityFixture fixture, bool healthy)
    {
        var recipe = ChunkMultiblockReadByteRecipe.Capture(fixture, healthy);
        await PhaseAsync(fixture, () => ShortAsync(fixture, recipe));
        await PhaseAsync(fixture, () => ExactAsync(fixture, recipe, healthy));
        await ChunkMultiblockBoundaryRows.RequireAsync(fixture, healthy);
        await ChunkSelectedIntegrityAssertions.Replay(fixture);
    }

    private static async Task PhaseAsync(ChunkSelectedIntegrityFixture fixture, Func<Task> operation)
    {
        var before = SampleRawIntegrityAssertions.Capture(fixture.Store);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, before), failures);
        await ServerFailureObserver.ObserveAsync(() => ChunkSelectedIntegrityAssertions.Replay(fixture), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ShortAsync(ChunkSelectedIntegrityFixture fixture, ChunkMultiblockReadByteRecipe recipe)
    {
        var budget = ChunkMultiblockReadBudgetCalls.Budget(fixture, recipe.TotalBytes - OneByte);
        var refusal = Assert.ThrowsExactly<KeyLoadException>(() => ChunkMultiblockReadBudgetCalls.Read(fixture, budget));
        await Assert.That(refusal.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(refusal.Message).IsEqualTo(global::KeyLoad.Core.ReadExecutionBudget.ReadBytesExceeded);
        await Assert.That(refusal.InnerException).IsNull();
        await Assert.That(budget.ReadBytes).IsEqualTo(recipe.FirstBytes);
    }

    private static async Task ExactAsync(ChunkSelectedIntegrityFixture fixture, ChunkMultiblockReadByteRecipe recipe, bool healthy)
    {
        var budget = ChunkMultiblockReadBudgetCalls.Budget(fixture, recipe.TotalBytes);
        var rows = ChunkMultiblockReadBudgetCalls.Read(fixture, budget);
        await Assert.That(budget.ReadBytes).IsEqualTo(recipe.TotalBytes);
        await Assert.That(JsonSerializer.Serialize(rows, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(ChunkMultiblockBoundaryData.Expected(healthy), JsonDefaults.Options));
    }
}
