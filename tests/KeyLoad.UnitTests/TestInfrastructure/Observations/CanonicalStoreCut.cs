namespace KeyLoad.UnitTests;

/// <summary>One actual canonical store view, including physical identity and complete retained bytes.</summary>
internal sealed record CanonicalStoreCut(byte[] Identity, long Position, long Applied, string[] Rows)
{
    internal static CanonicalStoreCut Read(TestDatabase database) => database.Store.Read(view =>
        new CanonicalStoreCut(OrleansSerialization.Serialize(database.Store.Identity), database.Store.Position,
            view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes ? OrleansSerialization.Deserialize<long>(bytes) : 0L,
            CanonicalStoreImage.Bytes(view)));

    internal async Task RequireAsync(CanonicalStoreCut actual)
    {
        await Assert.That(actual.Identity.AsSpan().SequenceEqual(Identity)).IsTrue();
        await Assert.That(actual.Position).IsEqualTo(Position);
        await Assert.That(actual.Applied).IsEqualTo(Applied);
        await Assert.That(actual.Rows.SequenceEqual(Rows, StringComparer.Ordinal)).IsTrue();
    }
}
