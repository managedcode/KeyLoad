using System.Text.Json;

namespace KeyLoad.UnitTests.TimeSeries;

internal static class ChunkSelectedBlockIntegrityAssertions
{
    internal static async Task OnlyOwnedRowChanged(ChunkSelectedIntegrityFixture fixture,
        SampleRawIntegrityCut original, SampleRawIntegrityCut damaged, SampleChunkCorruptionCut fault)
    {
        await Assert.That(damaged.Position).IsEqualTo(original.Position + 1);
        await Assert.That(damaged.Identity.SequenceEqual(original.Identity)).IsTrue();
        var before = JsonSerializer.Deserialize<string[][]>(original.Image)!;
        var after = JsonSerializer.Deserialize<string[][]>(damaged.Image)!;
        var key = Convert.ToHexString(ChunkSelectedBlockIntegrityDamage.Key(fixture));
        await Assert.That(before.Count(row => row[0] == key)).IsEqualTo(1);
        if (fault == SampleChunkCorruptionCut.MissingBlock)
        {
            var expected = before.Where(row => row[0] != key).ToArray();
            await Assert.That(JsonSerializer.Serialize(after)).IsEqualTo(JsonSerializer.Serialize(expected));
        }
        else
        {
            await Assert.That(after.Length).IsEqualTo(before.Length);
            for (var index = 0; index < before.Length; index++)
            {
                await Assert.That(after[index][0]).IsEqualTo(before[index][0]);
                if (before[index][0] == key)
                {
                    await ChangedByte(before[index][1], after[index][1]);
                }
                else
                {
                    await Assert.That(after[index][1]).IsEqualTo(before[index][1]);
                }
            }
        }
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, damaged);
    }

    private static async Task ChangedByte(string before, string after)
    {
        var original = Convert.FromHexString(before);
        var changed = Convert.FromHexString(after);
        await Assert.That(changed.Length).IsEqualTo(original.Length);
        await Assert.That(changed[0]).IsEqualTo((byte)(original[0] ^ 1));
        await Assert.That(changed.AsSpan(1).SequenceEqual(original.AsSpan(1))).IsTrue();
    }
}
