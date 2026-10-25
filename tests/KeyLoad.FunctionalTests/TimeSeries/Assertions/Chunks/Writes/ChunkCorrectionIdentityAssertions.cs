using System.Text.Json;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class ChunkCorrectionIdentityAssertions
{
    internal static async Task OnlyOwnedRowChanged(ChunkSelectedIntegrityFixture fixture,
        SampleRawIntegrityCut original, SampleRawIntegrityCut damaged)
    {
        await Assert.That(damaged.Position).IsEqualTo(original.Position + 1);
        await Assert.That(damaged.Identity.SequenceEqual(original.Identity)).IsTrue();
        var before = JsonSerializer.Deserialize<string[][]>(original.Image)!;
        var after = JsonSerializer.Deserialize<string[][]>(damaged.Image)!;
        var key = Convert.ToHexString(ChunkCorrectionIdentityTrial.Key(fixture));
        await Assert.That(after.Length).IsEqualTo(before.Length);
        await Assert.That(before.Count(row => row[0] == key)).IsEqualTo(1);
        for (var index = 0; index < before.Length; index++)
        {
            await Assert.That(after[index][0]).IsEqualTo(before[index][0]);
            if (before[index][0] == key)
            {
                await Assert.That(after[index][1] == before[index][1]).IsFalse();
            }
            else
            {
                await Assert.That(after[index][1]).IsEqualTo(before[index][1]);
            }
        }
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, damaged);
    }
}
