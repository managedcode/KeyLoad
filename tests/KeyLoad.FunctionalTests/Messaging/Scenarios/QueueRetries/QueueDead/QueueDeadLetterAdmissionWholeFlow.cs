using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueDeadLetterAdmissionWholeFlow
{
    internal static async Task Run(QueueDeadLetterAdmissionFault fault, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var fixture = new TestDatabase();
        var state = await QueueDeadLetterSelectedSetup.SeedAsync(fixture, QueueDeadLetterSelectedOperation.Cancel,
            QueueDeadLetterSelectedFault.IndexAlias);
        var originalImage = QueueWholeFlowStorage.Bytes(fixture.Store);
        var originalPosition = fixture.Store.Position;
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        var row = QueueDeadLetterAdmissionRows.Damage(fixture.Store, state, fault);
        var position = await NativeStoreAssertions.AdvancedAsync(fixture.Store, originalPosition);
        var damagedImage = QueueWholeFlowStorage.Bytes(fixture.Store);
        await QueueDeadLetterAdmissionAssertions.Image(fixture.Store, position, damagedImage);
        await QueueDeadLetterAdmissionAssertions.Refuse(fixture.Database, fixture.Store, state, fault, position, damagedImage);
        await QueueDeadLetterAdmissionAssertions.Refuse(fixture.Database, fixture.Store, state, fault, position, damagedImage);
        fixture.Store.Dispose();
        QueueDeadLetterSelectedSaved[] history;
        QueueDeadLetterSelectedSaved[] continuation;
        string[] finalImage;
        long finalPosition;
        using (var cold = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution))
        {
            position = await NativeStoreAssertions.ReopenedAsync(cold, position);
            var owner = fixture.CreateConfiguredDatabase(cold);
            await Assert.That(OrleansSerialization.Serialize(cold.Identity).SequenceEqual(identity)).IsTrue();
            await QueueDeadLetterAdmissionAssertions.Refuse(owner, cold, state, fault, position, damagedImage);
            QueueDeadLetterAdmissionRows.Repair(cold, row);
            position = await NativeStoreAssertions.AdvancedAsync(cold, position);
            await QueueDeadLetterAdmissionAssertions.Image(cold, position, originalImage);
            await QueueDeadLetterSelectedImage.RequireAsync(owner, cold, state);
            await QueueDeadLetterSelectedHistory.ReplayAsync(owner, cold, state, state.History);
            continuation = (await QueueDeadLetterSelectedContinuation.RunAsync(owner, cold, state)).ToArray();
            history = [.. state.History, .. continuation];
            await QueueDeadLetterSelectedHistory.ReplayAsync(owner, cold, state, history);
            await QueueDeadLetterSelectedRetainedInbox.RequireAsync(owner, cold, state);
            finalImage = QueueWholeFlowStorage.Bytes(cold);
            finalPosition = cold.Position;
        }
        using var second = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution);
        finalPosition = await NativeStoreAssertions.ReopenedAsync(second, finalPosition);
        var secondOwner = fixture.CreateConfiguredDatabase(second);
        await Assert.That(OrleansSerialization.Serialize(second.Identity).SequenceEqual(identity)).IsTrue();
        await QueueDeadLetterAdmissionAssertions.Image(second, finalPosition, finalImage);
        await QueueDeadLetterSelectedImage.RequireAsync(secondOwner, second, state, settled: true, healthy: true);
        await QueueDeadLetterSelectedHistory.ReplayAsync(secondOwner, second, state, history);
        await QueueDeadLetterSelectedContinuation.ColdEffectsAsync(secondOwner, second, continuation, state.DocumentLifetime);
        await QueueDeadLetterSelectedRetainedInbox.RequireAsync(secondOwner, second, state);
        await QueueDeadLetterAdmissionAssertions.Image(second, finalPosition, finalImage);
    }
}
