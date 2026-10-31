using global::Orleans.Serialization;
using global::Orleans.Storage;
using KeyLoad.Clustering;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.FunctionalTests.EventStreams;

internal sealed class EventFeedPubSubStorageTests
{
    private const int Version = 1;
    private const long FirstRevision = 1;
    private const long SecondRevision = 2;
    private const long Coverage = 3;
    private const string FirstKey = "/first";
    private const string SecondKey = "/second";
    private const string WrongStateName = "foreign-state";

    [Test]
    public async Task ActualPubSubCasCancellationAndDisposeRefuseThenHealthyStateRemainsComplete()
    {
        var registrations = new ServiceCollection();
        registrations.AddSerializer(builder => builder.AddAssembly(typeof(EventFeedWakeupHint).Assembly));
        await using var services = registrations.BuildServiceProvider();
        using var storage = new EventFeedPubSubStorage(services);
        var firstId = Id(FirstKey);
        var secondId = Id(SecondKey);
        var firstValue = new EventFeedWakeupHint(Version, Guid.NewGuid(), FirstRevision, Coverage);
        var first = new GrainState<EventFeedWakeupHint>(firstValue);
        await storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, firstId, first);
        var originalTag = first.ETag;
        await RequireState(storage, firstId, firstValue, originalTag);
        var stale = new GrainState<EventFeedWakeupHint>(firstValue);
        await RequireConflict(() => storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, firstId, stale));
        var secondValue = firstValue with
        {
            MapRevision = SecondRevision
        };
        var second = new GrainState<EventFeedWakeupHint>(secondValue) { ETag = null, RecordExists = false };
        await RequireCode(ErrorCode.Validation,
            () => storage.ReadStateAsync(WrongStateName, firstId, new GrainState<EventFeedWakeupHint>()));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await RequireCancelled(() => storage.ClearStateAsync(EventFeedPubSubProtocol.StateName,
            firstId, first, cancelled.Token));
        await RequireState(storage, firstId, firstValue, originalTag);
        await Assert.That(second.ETag).IsNull();
        await Assert.That(second.RecordExists).IsFalse();
        await storage.ClearStateAsync(EventFeedPubSubProtocol.StateName, firstId, first);
        await storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, secondId, second);
        await RequireState(storage, secondId, secondValue, second.ETag);
        storage.Dispose();
        await RequireDisposed(() => storage.ReadStateAsync(EventFeedPubSubProtocol.StateName,
            secondId, new GrainState<EventFeedWakeupHint>()));
    }

    private static GrainId Id(string key) => EventFeedPubSubFreshStateFixture.IdFor(key);

    private static async Task RequireState(EventFeedPubSubStorage storage, GrainId id,
        EventFeedWakeupHint expected, string? tag)
    {
        var read = new GrainState<EventFeedWakeupHint>();
        await storage.ReadStateAsync(EventFeedPubSubProtocol.StateName, id, read);
        await Assert.That(read.State).IsEqualTo(expected);
        await Assert.That(read.ETag).IsEqualTo(tag);
        await Assert.That(read.RecordExists).IsTrue();
    }

    private static async Task RequireCode(ErrorCode expected, Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (KeyLoadException failure) when (failure.Code == expected)
        {
            return;
        }
        throw new InvalidOperationException("The execution pubsub operation did not preserve its closed refusal.");
    }

    private static async Task RequireConflict(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (InconsistentStateException)
        {
            return;
        }
        throw new InvalidOperationException("The execution pubsub operation accepted a stale state revision.");
    }

    private static async Task RequireCancelled(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        throw new InvalidOperationException("The execution pubsub operation ignored original cancellation.");
    }

    private static async Task RequireDisposed(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        throw new InvalidOperationException("The execution pubsub operation entered after disposal.");
    }
}
