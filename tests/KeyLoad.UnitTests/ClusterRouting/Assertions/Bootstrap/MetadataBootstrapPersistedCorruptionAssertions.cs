using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class MetadataBootstrapPersistedCorruptionAssertions
{
    internal static async Task RefusedAsync(MetadataBootstrapState state, ReplicatedOperation attempt,
        string[] image, long position, string expectedMessage)
    {
        var read = Assert.ThrowsExactly<KeyLoadException>(() =>
            state.Fixture.Database.ReadPhysicalShardCatalog(MetadataBootstrapState.Actor));
        await Assert.That(read.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(read.Message).IsEqualTo(expectedMessage);
        var bootstrap = Assert.ThrowsExactly<KeyLoadException>(() => state.Fixture.Database.Apply(attempt));
        await Assert.That(bootstrap.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(bootstrap.Message).IsEqualTo(expectedMessage);
        await Assert.That(state.Fixture.Store.Read(view =>
            view.ReadOwnedValue(KeySpace.GlobalOutcome(MetadataBootstrapState.Actor, attempt.Id)))).IsNull();
        await OriginalsAsync(state);
        await MetadataBootstrapValues.ConservedAsync(state, image, state.Identity, position);
    }

    internal static async Task OriginalsAsync(MetadataBootstrapState state)
    {
        for (var index = 0; index < state.Originals.Length; index++)
        {
            await Assert.That(MetadataBootstrapValues.Outcome(state, state.Originals[index])
                .AsSpan().SequenceEqual(state.Outcomes[index])).IsTrue();
        }
        await Assert.That(MetadataBootstrapValues.Outcome(state, state.Fresh).AsSpan().SequenceEqual(state.FreshOutcome)).IsTrue();
        await Assert.That(MetadataBootstrapValues.Outcome(state, state.Command).AsSpan().SequenceEqual(state.CommandOutcome)).IsTrue();
    }
}
