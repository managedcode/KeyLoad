using System.Text.Json;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlModelParserConservation
{
    private const int Bound = 4096;
    private const string ImageBound = "The complete parser model fixture image exceeds its bound.";

    internal static SqlModelParserStoreCut Capture(SqlModelParserOperationFixture fixture)
        => new(fixture.Store.Position, NativeSerialization.Serialize(fixture.Store.Identity), fixture.Store.Read(view =>
        {
            var page = view.Scan([], Bound);
            if (page.HasMore)
            {
                throw new InvalidOperationException(ImageBound);
            }
            return JsonSerializer.Serialize(page.Records.Select(record => new[]
                { Convert.ToHexString(record.Key.Span), Convert.ToHexString(record.Value.Span) }).ToArray());
        }));
    internal static async Task SameAsync(SqlModelParserOperationFixture fixture, SqlModelParserStoreCut cut)
    {
        var current = Capture(fixture);
        await Assert.That(current.Position).IsEqualTo(cut.Position);
        await Assert.That(current.Identity.SequenceEqual(cut.Identity)).IsTrue();
        await Assert.That(current.Image).IsEqualTo(cut.Image);
    }
    internal static async Task ReplayAsync(SqlModelParserOperationFixture fixture)
    {
        var before = Capture(fixture);
        await Assert.That(fixture.Original.CommandId).IsEqualTo(fixture.Seed.CommandId);
        var replay = fixture.Owner.Apply(fixture.Seed).Get<CommitReceipt>();
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(fixture.Original))).IsTrue();
        await SameAsync(fixture, before);
    }
}
