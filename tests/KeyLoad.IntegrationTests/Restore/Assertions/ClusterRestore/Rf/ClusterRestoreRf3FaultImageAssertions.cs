namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3FaultImageAssertions
{
    internal static async Task RequireAsync(ClusterRestoreRf3Fixture target,
        (byte[] Operation, byte[]? Target) original)
    {
        var actual = ClusterRestoreRf3FaultImage.Capture(target);
        await Assert.That(actual.Operation.SequenceEqual(original.Operation)).IsTrue();
        await Assert.That(actual.Target is null).IsEqualTo(original.Target is null);
        if (original.Target is not null)
        {
            await Assert.That(actual.Target is not null
                && actual.Target.SequenceEqual(original.Target)).IsTrue();
        }
    }
}
