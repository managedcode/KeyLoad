using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record PartitionHostApplicationCut(byte[] Identity, long Position, string[] Rows)
{
    internal static PartitionHostApplicationCut Capture(IAtomicStore store)
        => store.Read<PartitionHostApplicationCut>(view =>
        {
            var page = view.Scan([], UnitExecutionOptions.RecoveryImageCapture().Value.MaximumRecords);
            if (page.HasMore)
            {
                throw new InvalidOperationException(PartitionHostApplicationProtocol.IncompleteCut);
            }
            return new(NativeSerialization.Serialize(store.Identity), store.Position,
                page.Records.Select(row => Encode(row.Key.Span, row.Value.Span)).ToArray());
        });

    internal async Task RequireAsync(IAtomicStore store)
    {
        var actual = Capture(store);
        await Assert.That(actual.Identity.AsSpan().SequenceEqual(Identity)).IsTrue();
        await Assert.That(actual.Position).IsEqualTo(Position);
        await Assert.That(actual.Rows.SequenceEqual(Rows, StringComparer.Ordinal)).IsTrue();
    }

    internal async Task<PartitionHostApplicationCut> CommitAsync(IAtomicStore store, byte[] key, byte[] value)
    {
        await RequireAsync(store).ConfigureAwait(false);
        await Assert.That(store.Read(view => view.ReadOwnedValue(key))).IsNull();
        var returned = store.Commit((transaction, proposedPosition) =>
        {
            transaction.Put(key, value);
            return proposedPosition;
        });
        var expectedPosition = checked(Position + PartitionHostApplicationProtocol.NextPosition);
        await Assert.That(returned).IsEqualTo(expectedPosition);
        var expectedRows = Rows.Append(Encode(key, value))
            .OrderBy(row => row[..row.IndexOf(PartitionHostApplicationProtocol.Separator, StringComparison.Ordinal)],
                StringComparer.Ordinal).ToArray();
        var expected = new PartitionHostApplicationCut(Identity, expectedPosition, expectedRows);
        await expected.RequireAsync(store).ConfigureAwait(false);
        return expected;
    }

    private static string Encode(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
        => Convert.ToHexString(key) + PartitionHostApplicationProtocol.Separator + Convert.ToHexString(value);
}
