
namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDispatchCasAssertions
{
    internal static async Task CurrentAsync(QueueDispatchCasFixture fixture, bool paused, long version)
    {
        var expected = fixture.Definition with { Paused = paused, SchemaVersion = version };
        await Assert.That(NativeSerialization.Serialize(fixture.Current())
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        await Assert.That(fixture.Document()!.SequenceEqual(fixture.OriginalDocument)).IsTrue();
        await Assert.That(fixture.Collection()!.SequenceEqual(fixture.OriginalCollection)).IsTrue();
    }

    internal static async Task SuccessAsync(OperationResult result, ResourceDefinition expected)
        => await Assert.That(NativeSerialization.Serialize(result.Get<ResourceDefinition>())
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();

    internal static async Task FailureAsync(QueueDispatchCasFixture fixture, OperationResult result,
        ErrorCode code, bool paused, long version)
    {
        await Assert.That(QueueDispatchCasFixture.Failure(result).Code).IsEqualTo(code);
        await CurrentAsync(fixture, paused, version);
    }
}
