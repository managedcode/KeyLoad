using KeyLoad.Server.Search;

namespace KeyLoad.FunctionalTests.Search;

internal static class TextOnlineSeedAssertions
{
    internal static async Task HealthyAsync(TestDatabase database, TextOnlineSeedFixture fixture,
        CancellationToken cancellationToken)
    {
        var budget = new ReadExecutionBudget(cancellationToken: cancellationToken);
        using var pin = TextOnlineSourcePin.Capture(database.Database,
            TextMaintenanceTestValues.Principal, fixture.Pin, budget);
        var seed = pin.ReadSeed();
        await Assert.That(JsonDefaults.Serialize(seed.Documents)
            .SequenceEqual(JsonDefaults.Serialize(fixture.Expected))).IsTrue();
    }
}
