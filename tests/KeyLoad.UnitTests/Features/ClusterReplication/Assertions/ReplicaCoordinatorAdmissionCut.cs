using KeyLoad.Storage;

namespace KeyLoad.UnitTests;

internal sealed record ReplicaCoordinatorAdmissionCut(long CanonicalPosition, long ReplicaPosition,
    long AppliedPosition, byte[] CanonicalRows, byte[] ReplicaRows, byte[] HardState)
{
    private const int MaximumFixtureRows = 512;

    internal static ReplicaCoordinatorAdmissionCut Read(ReplicaLifecycleFixture fixture)
        => new(fixture.Database.Store.Position, fixture.ReplicaStore.Position, fixture.Database.LastApplied,
            Rows(fixture.Database.Store), Rows(fixture.ReplicaStore), NativeSerialization.Serialize(fixture.Log.State));

    private static byte[] Rows(IAtomicStore store)
        => store.Read(view =>
        {
            var page = view.Scan([], MaximumFixtureRows, null);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The complete fixture cut exceeded its bounded row inventory.");
            }
            return NativeSerialization.Serialize(page);
        });

    internal async Task RequireAsync(ReplicaCoordinatorAdmissionCut actual)
    {
        await Assert.That(actual.CanonicalPosition).IsEqualTo(CanonicalPosition);
        await Assert.That(actual.ReplicaPosition).IsEqualTo(ReplicaPosition);
        await Assert.That(actual.AppliedPosition).IsEqualTo(AppliedPosition);
        await Assert.That(actual.CanonicalRows.AsSpan().SequenceEqual(CanonicalRows)).IsTrue();
        await Assert.That(actual.ReplicaRows.AsSpan().SequenceEqual(ReplicaRows)).IsTrue();
        await Assert.That(actual.HardState.AsSpan().SequenceEqual(HardState)).IsTrue();
    }
}
