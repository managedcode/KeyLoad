using KeyLoad.Server.Search;

namespace KeyLoad.UnitTests.Search;

internal static class TextOnlineSeedAssertions
{
    internal static async Task<long> HealthyAsync(TestDatabase database, TextOnlineSeedFixture fixture,
        CancellationToken cancellationToken)
    {
        var budget = new ReadExecutionBudget(cancellationToken: cancellationToken);
        using var pin = TextOnlineSourcePin.Capture(database.Database,
            TextMaintenanceTestValues.Principal, fixture.Pin, budget);
        var before = budget.ReadBytes;
        var seed = pin.ReadSeed();
        await Assert.That(JsonDefaults.Serialize(seed.Documents)
            .SequenceEqual(JsonDefaults.Serialize(fixture.Expected))).IsTrue();
        return before;
    }
}
