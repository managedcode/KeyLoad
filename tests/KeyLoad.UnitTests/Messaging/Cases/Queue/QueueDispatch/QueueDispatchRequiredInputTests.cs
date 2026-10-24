using System.Text.Json;

namespace KeyLoad.UnitTests.Messaging;

internal sealed class QueueDispatchRequiredInputTests
{
    private static readonly string[] RequiredFields =
    [nameof(QueueDispatchControlRequest.TenantId), nameof(QueueDispatchControlRequest.DatabaseId),
     nameof(QueueDispatchControlRequest.Queue), nameof(QueueDispatchControlRequest.ExpectedSchemaVersion),
     nameof(QueueDispatchControlRequest.Paused)];

    [Test]
    public async Task OmittedPauseOrVersionCannotBecomeAnImplicitResumeAndCompleteExplicitFalseRemainsValid()
    {
        var request = new QueueDispatchControlRequest("dispatch-input-tenant", "dispatch-input-database",
            "dispatch-input-queue", QueueDispatchCasFixture.InitialVersion, false);
        var complete = JsonSerializer.SerializeToNode(request, JsonDefaults.Options)!.AsObject();
        foreach (var field in RequiredFields)
        {
            var missing = complete.DeepClone().AsObject();
            var key = JsonDefaults.Options.PropertyNamingPolicy?.ConvertName(field) ?? field;
            await Assert.That(missing.Remove(key)).IsTrue();
            Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<QueueDispatchControlRequest>(
                missing.ToJsonString(), JsonDefaults.Options));
        }
        var actual = JsonSerializer.Deserialize<QueueDispatchControlRequest>(complete.ToJsonString(), JsonDefaults.Options);
        await Assert.That(actual).IsEqualTo(request);
    }
}
