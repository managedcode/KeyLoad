namespace KeyLoad.UnitTests.Search;

/// <summary>Checks the complete source and original child receipt, not only count or generation.</summary>
internal static class AnnSettledResultAssertions
{
    internal static async Task ExactAsync(AnnMaintenanceResult expected, AnnMaintenanceResult actual)
    {
        await Assert.That(actual.CommandId).IsEqualTo(expected.CommandId);
        await Assert.That(actual.Consumer).IsEqualTo(expected.Consumer);
        await Assert.That(actual.IndexGeneration).IsEqualTo(expected.IndexGeneration);
        await Assert.That(actual.Phase).IsEqualTo(expected.Phase);
        await Assert.That(actual.Count).IsEqualTo(expected.Count);
        await Assert.That(actual.IndexSha256).IsEqualTo(expected.IndexSha256);
        await Assert.That(actual.Source).IsEqualTo(expected.Source);
        await Assert.That(actual.Source!.CorpusSha256).IsEqualTo(expected.Source!.CorpusSha256);
        await Assert.That(actual.Source.DependencySha256).IsEqualTo(expected.Source.DependencySha256);
        await Assert.That(actual.Source.ReadGeneration).IsEqualTo(expected.Source.ReadGeneration);
        await Assert.That(actual.Source.OutboxFirstAvailable).IsEqualTo(expected.Source.OutboxFirstAvailable);
        await Assert.That(actual.Checkpoint!.Checkpoint).IsEqualTo(expected.Checkpoint!.Checkpoint);
        await Assert.That(actual.Checkpoint.AlreadyProcessed).IsEqualTo(expected.Checkpoint.AlreadyProcessed);
        await Assert.That(actual.Checkpoint.Receipt.CommandId).IsEqualTo(expected.Checkpoint.Receipt.CommandId);
        await Assert.That(Convert.ToHexString(OrleansSerialization.Serialize(actual.Checkpoint)))
            .IsEqualTo(Convert.ToHexString(OrleansSerialization.Serialize(expected.Checkpoint)));
        await Assert.That(Convert.ToHexString(OrleansSerialization.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(OrleansSerialization.Serialize(expected)));
    }
}
