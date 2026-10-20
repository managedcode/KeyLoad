using System.Text.Json;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class VerifiedBackupMixedCutState
{
    internal static async Task StateAsync(EventingArtifactFixture fixture, DatabaseEngine database, bool secondDocument)
    {
        var page = database.ReadEventSource(EventingArtifactFixture.Principal, new(fixture.Events));
        await Assert.That(page.Head).IsEqualTo(new EventSourceHead(3, 1, 1));
        await Assert.That(page.Events.Select(item => item.Position)).IsEquivalentTo(new long[] { 1, 2, 3 }, CollectionOrdering.Matching);
        await Assert.That(page.Events.Select(item => item.EventSequence)).IsEquivalentTo(new long[] { 1, 2, 3 }, CollectionOrdering.Matching);
        await Assert.That(page.Events.Select(item => item.Data.EventId)).IsEquivalentTo(new[] { EventingArtifactFixture.FirstEvent, EventingArtifactFixture.SecondEvent, EventingArtifactFixture.ThirdEvent }, CollectionOrdering.Matching);
        await Assert.That(page.Events.All(item => item.Data.PayloadJson == EventingArtifactFixture.Json && item.RecordedAt == fixture.Time)).IsTrue();
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.CutPosition).IsEqualTo(database.Store.Position);
        var group = database.GetSubscription(EventingArtifactFixture.Principal, fixture.Group);
        var expectedGroup = new SubscriptionInfo(fixture.Group, new(EventingArtifactFixture.Principal), 1, 1, 1, 3, 3, false, null);
        await Assert.That(JsonSerializer.Serialize(group, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(expectedGroup, JsonDefaults.Options));
        var other = database.GetSubscription(EventingArtifactFixture.Principal, fixture.Other);
        var expectedOther = new SubscriptionInfo(fixture.Other, new(EventingArtifactFixture.Principal), 1, 1, 0, 1, 3, false, null);
        await Assert.That(JsonSerializer.Serialize(other, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(expectedOther, JsonDefaults.Options));
        var document = database.GetDocument(EventingArtifactFixture.Principal,
            new(fixture.Source.Partition, EventingArtifactFixture.Documents, EventingArtifactFixture.DocumentId));
        await Assert.That(document!.Reference).IsEqualTo(fixture.Source.ExpectedReference(
            new(fixture.Source.Partition, EventingArtifactFixture.Documents, EventingArtifactFixture.DocumentId)));
        await Assert.That(document.Redacted).IsFalse();
        await Assert.That(document.RedactedFields).IsEmpty();
        await Assert.That(document.Revision).IsEqualTo(1L);
        await Assert.That(document.Json).IsEqualTo(EventingArtifactFixture.Json);
        var message = database.InspectMessage(EventingArtifactFixture.Principal, fixture.Lane, EventingArtifactFixture.MessageId);
        var expectedMessage = new MessageInspection(new(EventingArtifactFixture.MessageId, MessageState.Leased, 1, 2, 1,
            null, null, EventingArtifactFixture.Principal, 1, fixture.Time.AddSeconds(30)), EventingArtifactFixture.Json, EventingArtifactFixture.EmptyJson);
        await Assert.That(JsonSerializer.Serialize(message, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(expectedMessage, JsonDefaults.Options));
        await Assert.That(database.GetOutboxStatus(EventingArtifactFixture.Principal, fixture.Source.Partition).Head.Tail).IsEqualTo(secondDocument ? 4L : 3L);

        var second = database.GetDocument(EventingArtifactFixture.Principal,
            new(fixture.Source.Partition, EventingArtifactFixture.Documents, VerifiedBackupCutPair.DocumentId));
        if (!secondDocument)
        {
            await Assert.That(second).IsNull();
            return;
        }
        await Assert.That(second).IsNotNull();
        await Assert.That(second!.Reference).IsEqualTo(fixture.Source.ExpectedReference(new(fixture.Source.Partition,
            EventingArtifactFixture.Documents, VerifiedBackupCutPair.DocumentId)));
        await Assert.That(second.Revision).IsEqualTo(1L);
        await Assert.That(second.Redacted).IsFalse();
        await Assert.That(second.RedactedFields).IsEmpty();
        await Assert.That(second.Json).IsEqualTo(VerifiedBackupCutPair.DocumentJson);
    }
}
