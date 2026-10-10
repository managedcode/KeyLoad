using global::Orleans.Serialization;
using global::Orleans.Storage;
using KeyLoad.Orleans;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class EventFeedPubSubStorageTests
{
    private const int SingleSlot = 1;
    private const int Version = 1;
    private const long FirstRevision = 1;
    private const long SecondRevision = 2;
    private const long Coverage = 3;
    private const string FirstKey = "/first";
    private const string SecondKey = "/second";
    private const string WrongStateName = "foreign-state";

    [Test]
    public async Task ActualNativePubSubCasQuotaCancellationAndDisposeRefuseThenHealthyStateRemainsComplete()
    {
        var registrations = new ServiceCollection();
        var fixtureLimits = new DatabaseLimits { MaxResults = SingleSlot };
        fixtureLimits.Validate();
        registrations.AddSingleton<IOptions<DatabaseLimits>>(Options.Create(fixtureLimits));
        registrations.AddOptions<GrainRoutingOptions>();
        registrations.AddSerializer(builder => builder.AddAssembly(typeof(EventFeedWakeupHint).Assembly));
        await using var services = registrations.BuildServiceProvider();
        using var storage = new EventFeedPubSubStorage(services,
            services.GetRequiredService<IOptions<DatabaseLimits>>(),
            services.GetRequiredService<IOptions<GrainRoutingOptions>>());
        var firstId = Id(FirstKey);
        var secondId = Id(SecondKey);
        var firstValue = new EventFeedWakeupHint(Version, Guid.NewGuid(), FirstRevision, Coverage);
        var first = new GrainState<EventFeedWakeupHint>(firstValue);
        await storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, firstId, first);
        var originalTag = first.ETag;
        await RequireState(storage, firstId, firstValue, originalTag);
        var stale = new GrainState<EventFeedWakeupHint>(firstValue);
        await RequireConflict(() => storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, firstId, stale));
        var secondValue = firstValue with { MapRevision = SecondRevision };
        var second = new GrainState<EventFeedWakeupHint>(secondValue) { ETag = null, RecordExists = false };
        await RequireCode(ErrorCode.ResourceExhausted,
            () => storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, secondId, second));
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

    [Test]
    public async Task ActualNativePubSubEncodedByteRefusalPreservesOriginalStateThenHealthyReplacementAndRestart()
    {
        const string Original = "original-hint-state";
        const string Healthy = "healthy-complete-hint-state";
        const char OversizeData = 'x';
        var registrations = new ServiceCollection();
        registrations.AddOptions<GrainRoutingOptions>();
        registrations.AddSerializer(builder => builder.AddAssembly(typeof(EventFeedWakeupHint).Assembly));
        await using var services = registrations.BuildServiceProvider();
        var routing = services.GetRequiredService<IOptions<GrainRoutingOptions>>();
        var defaults = Options.Create(new DatabaseLimits());
        var id = Id(FirstKey);
        var legalBytes = EventFeedPubSubRowEncoding.Create(services, id, Healthy, defaults,
            routing, CancellationToken.None).EncodedBytes;
        var limits = new DatabaseLimits
        {
            MaxResults = SingleSlot,
            MaxBatchBytes = checked((int)legalBytes),
            MaxQueryReadBytes = legalBytes
        };
        limits.Validate();
        var options = Options.Create(limits);
        using (var storage = new EventFeedPubSubStorage(services, options, routing))
        {
            var original = new GrainState<string>(Original);
            await storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, id, original);
            var originalTag = original.ETag;
            var refused = new GrainState<string>(new string(OversizeData, checked((int)legalBytes)))
            { ETag = originalTag, RecordExists = true };
            await RequireCode(ErrorCode.ResourceExhausted,
                () => storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, id, refused));
            var read = new GrainState<string>();
            await storage.ReadStateAsync(EventFeedPubSubProtocol.StateName, id, read);
            await Assert.That(read.State).IsEqualTo(Original);
            await Assert.That(read.ETag).IsEqualTo(originalTag);
            await Assert.That(read.RecordExists).IsTrue();
            await Assert.That(refused.ETag).IsEqualTo(originalTag);
            var healthy = new GrainState<string>(Healthy) { ETag = originalTag, RecordExists = true };
            await storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, id, healthy);
            await storage.ReadStateAsync(EventFeedPubSubProtocol.StateName, id, read);
            await Assert.That(read.State).IsEqualTo(Healthy);
            await Assert.That(read.ETag).IsEqualTo(healthy.ETag);
            await Assert.That(read.RecordExists).IsTrue();
        }
        using var restarted = new EventFeedPubSubStorage(services, options, routing);
        var absent = new GrainState<string>();
        await restarted.ReadStateAsync(EventFeedPubSubProtocol.StateName, id, absent);
        await Assert.That(absent.ETag).IsNull();
        await Assert.That(absent.RecordExists).IsFalse();
        var fresh = new GrainState<string>(Healthy);
        await restarted.WriteStateAsync(EventFeedPubSubProtocol.StateName, id, fresh);
        await restarted.ReadStateAsync(EventFeedPubSubProtocol.StateName, id, absent);
        await Assert.That(absent.State).IsEqualTo(Healthy);
        await Assert.That(absent.ETag).IsEqualTo(fresh.ETag);
        await Assert.That(absent.RecordExists).IsTrue();
    }

    private static GrainId Id(string key) => GrainId.Create(EventFeedPubSubProtocol.GrainTypeName,
        EventFeedPubSubProtocol.ProviderName + key);

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
        { await operation(); }
        catch (KeyLoadException failure) when (failure.Code == expected) { return; }
        throw new InvalidOperationException("The native pubsub operation did not preserve its closed refusal.");
    }

    private static async Task RequireConflict(Func<Task> operation)
    {
        try
        { await operation(); }
        catch (InconsistentStateException) { return; }
        throw new InvalidOperationException("The native pubsub operation accepted a stale state revision.");
    }

    private static async Task RequireCancelled(Func<Task> operation)
    {
        try
        { await operation(); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("The native pubsub operation ignored original cancellation.");
    }

    private static async Task RequireDisposed(Func<Task> operation)
    {
        try
        { await operation(); }
        catch (ObjectDisposedException) { return; }
        throw new InvalidOperationException("The native pubsub operation entered after disposal.");
    }
}
