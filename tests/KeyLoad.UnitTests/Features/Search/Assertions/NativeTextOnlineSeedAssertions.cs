using KeyLoad.Core;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextOnlineSeedAssertions
{
    internal static async Task<long> HealthyAsync(TestDatabase database, NativeTextOnlineSeedFixture fixture,
        CancellationToken cancellationToken)
    {
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            cancellationToken: cancellationToken);
        using var pin = NativeTextOnlineSourcePin.Capture(database.Database,
            NativeTextMaintenanceTestValues.Principal, fixture.Pin, budget);
        var before = budget.ReadBytes;
        var seed = pin.ReadSeed();
        await Assert.That(JsonDefaults.Serialize(seed.Documents)
            .SequenceEqual(JsonDefaults.Serialize(fixture.Expected))).IsTrue();
        return before;
    }
}
