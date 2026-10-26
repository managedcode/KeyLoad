using System.Globalization;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class ReadCutLeafBoundaryTests
{
    private const int RecordCount = 2000;
    private static readonly byte[] Payload = [0x41];

    [Test]
    public async Task CapturedPrefixesKeepEveryRecordAcrossFrozenLeafBoundaries()
    {
        using var fixture = new ReadCutFixture();
        fixture.Store.Commit((transaction, _) =>
        {
            for (var index = 0; index < RecordCount; index++)
            {
                transaction.Put(Key(index), Payload);
            }
            return true;
        });
        var position = fixture.Store.Position;
        for (var index = 0; index < RecordCount; index++)
        {
            var before = fixture.Store.Read(view => view.Scan(Prefix(index), 2));
            await Assert.That(before.Records.Length).IsEqualTo(1);
            await Assert.That(before.Records[0].Key.Span.SequenceEqual(Key(index))).IsTrue();
            await Assert.That(before.Records[0].Value.Span.SequenceEqual(Payload)).IsTrue();
            await Assert.That(before.HasMore).IsFalse();
        }
        using var captured = fixture.Capture(ReadCutFixture.Limits(RecordCount, 1_000_000));
        await Assert.That(captured.Cut.Position).IsEqualTo(position);
        for (var index = 0; index < RecordCount; index++)
        {
            var rows = new List<(byte[] Key, byte[] Value)>();
            var result = captured.VisitPrefix(Prefix(index), (key, value) =>
            {
                rows.Add((key.ToArray(), value.ToArray()));
                return true;
            });
            await Assert.That(rows.Count).IsEqualTo(1);
            await Assert.That(rows[0].Key.SequenceEqual(Key(index))).IsTrue();
            await Assert.That(rows[0].Value.SequenceEqual(Payload)).IsTrue();
            await Assert.That(result.Records).IsEqualTo(index + 1);
            await Assert.That(result.HasMore).IsFalse();
        }
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
    }

    private static byte[] Prefix(int index)
        => ReadCutFixture.Key(index.ToString("D4", CultureInfo.InvariantCulture) + "/");

    private static byte[] Key(int index) => [.. Prefix(index), (byte)'v'];
}
