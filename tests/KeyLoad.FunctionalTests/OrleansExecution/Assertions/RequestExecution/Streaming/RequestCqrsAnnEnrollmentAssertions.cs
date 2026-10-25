namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class RequestCqrsAnnEnrollmentAssertions
{
    internal static async Task RequireAsync(RequestCqrsHostedRuntimeFixture fixture, AnnMaintenanceRequest original)
    {
        var actual = fixture.Database.Database.GetOutboxStatus(RequestCqrsAnnTestData.Principal,
            original.Consumer.Partition).Consumers.Single(value => value.Consumer == original.Consumer);
        await Assert.That(actual.Released).IsFalse();
        await Assert.That(actual.InitialAnnMaintenance).IsNotNull();
        await Assert.That(OrleansSerialization.Serialize(actual.InitialAnnMaintenance!).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(original))).IsTrue();
        await Assert.That(actual.Definition.AnnMaintenance).IsNotNull();
        await Assert.That(OrleansSerialization.Serialize(actual.Definition.AnnMaintenance!).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(original.MaintenanceProfile!))).IsTrue();
        await Assert.That(actual.MaintenanceAuthority).IsNotNull();
    }
}
