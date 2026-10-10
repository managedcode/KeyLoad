using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class EventingArtifactHealthyAssertions
{
    private const string HealthyId = "healthy";

    internal static async Task EventAsync(EventingArtifactFixture fixture, SourceEventRecord actual)
    {
        var expected = new SourceEventRecord(fixture.Events, 4, 4,
            new(HealthyId, EventingArtifactFixture.EventType, EventingArtifactFixture.Json), fixture.Time);
        await Assert.That(actual).IsEqualTo(expected);
    }

    internal static async Task DeliveryAsync(EventingArtifactFixture fixture, Delivery actual)
    {
        await Assert.That(actual.Token).IsNotEmpty();
        var expected = new Delivery(HealthyId, EventingArtifactFixture.Json, EventingArtifactFixture.EmptyJson,
            string.Empty, 1, fixture.Time.AddSeconds(30), 1, 1);
        await Assert.That(actual with { Token = string.Empty }).IsEqualTo(expected);
    }

    internal static Task CompletedAsync(EventingArtifactFixture fixture, DatabaseEngine database)
        => CompletedAsync(fixture, database, new(new(EventingArtifactFixture.MessageId, MessageState.Leased, 1, 2, 1,
            null, null, EventingArtifactFixture.Principal, 1, fixture.Time.AddSeconds(30)),
            EventingArtifactFixture.Json, EventingArtifactFixture.EmptyJson));

    internal static Task CompletedAfterNaturalExpiryAsync(EventingArtifactFixture fixture, DatabaseEngine database)
        => CompletedAsync(fixture, database, new(new(EventingArtifactFixture.MessageId, MessageState.Ready, 1, 3, 3,
            null, null, LeaseVersion: 1), EventingArtifactFixture.Json, EventingArtifactFixture.EmptyJson));

    private static async Task CompletedAsync(EventingArtifactFixture fixture, DatabaseEngine database, MessageInspection original)
    {
        var acknowledged = database.InspectMessage(EventingArtifactFixture.Principal, fixture.Lane, HealthyId);
        var expected = new MessageInspection(new(HealthyId, MessageState.Acked, 1, 3, 2,
            null, null, LeaseVersion: 1), null, null);
        await Assert.That(JsonSerializer.Serialize(acknowledged, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
        var retained = database.InspectMessage(EventingArtifactFixture.Principal, fixture.Lane, EventingArtifactFixture.MessageId);
        await Assert.That(JsonSerializer.Serialize(retained, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(original, JsonDefaults.Options));
    }
}
