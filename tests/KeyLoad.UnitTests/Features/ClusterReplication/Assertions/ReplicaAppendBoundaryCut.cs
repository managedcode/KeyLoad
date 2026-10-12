namespace KeyLoad.UnitTests;

internal sealed record ReplicaAppendBoundaryCut(long CanonicalPosition, long ReplicaPosition,
    long AppliedPosition, byte[] CanonicalRows, byte[] ReplicaRows, byte[] HardState)
{
    private const int MaximumFixtureRows = 512;

    internal static ReplicaAppendBoundaryCut Read(ReplicaAppendBoundaryFixture fixture)
        => new(fixture.Canonical.Store.Position, fixture.Store.Position, fixture.Canonical.Database.LastApplied,
            Rows(fixture.Canonical.Store), Rows(fixture.Store), NativeSerialization.Serialize(fixture.Log.State));

    private static byte[] Rows(global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store) => store.Read(view =>
    {
        var page = view.Scan([], MaximumFixtureRows, null);
        if (page.HasMore)
        { throw new InvalidOperationException("The complete native log fixture cut exceeded its bounded inventory."); }
        return NativeSerialization.Serialize(page);
    });

    internal async Task RequireCanonicalAsync(ReplicaAppendBoundaryCut actual)
    {
        await Assert.That(actual.CanonicalPosition).IsEqualTo(CanonicalPosition);
        await Assert.That(actual.AppliedPosition).IsEqualTo(AppliedPosition);
        await RequireBytesAsync(CanonicalRows, actual.CanonicalRows);
    }

    internal async Task RequireAsync(ReplicaAppendBoundaryCut actual)
    {
        await RequireCanonicalAsync(actual);
        await Assert.That(actual.ReplicaPosition).IsEqualTo(ReplicaPosition);
        await RequireBytesAsync(ReplicaRows, actual.ReplicaRows);
        await RequireBytesAsync(HardState, actual.HardState);
    }

    internal static async Task RequireBytesAsync(byte[] expected, byte[] actual)
        => await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();
}
