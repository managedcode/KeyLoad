using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.BackupRestore;

internal static class RestoreCommittedDispatchAssertions
{
    private const string SystemSpace = "system";
    private const string DispatchKey = "dispatch-paused";
    private const string MissingAuthority = "The complete restore flow requires committed dispatch authority.";

    internal static async Task SourceAsync(EventingArtifactFixture fixture)
    {
        var source = fixture.Source.Store;
        var before = source.Position;
        var operation = fixture.Operation(OperationKind.SetDispatch, false, Guid.NewGuid());
        var result = fixture.SubmitIssued(fixture.Source.Database, operation);
        await Assert.That(result.Get<bool>()).IsTrue();
        await Assert.That(source.Position).IsEqualTo(before + 1);
        await AuthorityAsync(source, false);
        var position = source.Position;
        var image = EventingArtifactState.FullBytes(source);
        await EventingArtifactState.SameResultAsync(result,
            fixture.SubmitIssued(fixture.Source.Database, operation));
        await Assert.That(source.Position).IsEqualTo(position);
        await Assert.That(EventingArtifactState.FullBytes(source))
            .IsEquivalentTo(image, CollectionOrdering.Matching);
    }

    internal static Task PausedAsync(EventingArtifactFixture fixture)
        => AuthorityAsync(fixture.Target ?? throw new InvalidOperationException(MissingAuthority), true);

    private static async Task AuthorityAsync(global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, bool expected)
    {
        var bytes = store.Read(view => view.ReadOwnedValue(KeyCodec.Encode(SystemSpace, DispatchKey)))
            ?? throw new InvalidOperationException(MissingAuthority);
        await Assert.That(OrleansSerialization.Deserialize<bool>(bytes)).IsEqualTo(expected);
        await Assert.That(bytes.AsSpan().SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();
    }
}
