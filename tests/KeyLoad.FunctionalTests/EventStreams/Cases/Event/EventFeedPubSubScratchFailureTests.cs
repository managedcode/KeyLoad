using KeyLoad.Clustering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.FunctionalTests.EventStreams;

internal sealed class EventFeedPubSubScratchFailureTests
{
    private const int ScratchBytes = 256;
    private const string Original = "original-execution-advisory-state";
    private const string Healthy = "healthy-execution-advisory-state";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualScratchRefusalPreservesCauseAndFollowingWriterCancellationAndDisposal(bool memory)
    {
        var routing = Routing();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        using var writer = new EventFeedPubSubCountingWriter(ScratchBytes, routing, cancellation.Token);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => Request(writer, memory, ScratchBytes + 1));
        await RequireCapacity(failure);
        await Assert.That(writer.Length).IsEqualTo(0L);
        Request(writer, memory, 1);
        writer.Advance(1);
        await Assert.That(writer.Length).IsEqualTo(1L);
        await cancellation.CancelAsync();
        var cancelled = Assert.ThrowsExactly<OperationCanceledException>(() => Request(writer, memory, 1));
        await Assert.That(cancelled.CancellationToken).IsEqualTo(cancellation.Token);
        writer.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => Request(writer, memory, 1));
    }

    [Test]
    public async Task ActualRegisteredSerializerScratchRefusalKeepsCompleteCasStateAndHealthyReplacement()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var registrations = new ServiceCollection();
        registrations.AddSerializer(builder => builder.AddAssembly(typeof(EventFeedWakeupHint).Assembly));
        var failures = new List<Exception>();
        try
        {
            await using var services = registrations.BuildServiceProvider();
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var routing = Routing(4096);
                var limits = Options.Create(new DatabaseLimits());
                var storageFailures = new List<Exception>();
                try
                {
                    using var storage = new EventFeedPubSubStorage(services, routing);
                    await KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                        () => RequireRegisteredAsync(storage, routing, token), storageFailures);
                }
                catch (Exception cleanup) when (CqrsBoundaryErrors.IsNonFatal(cleanup))
                {
                    storageFailures.Add(cleanup);
                }
                catch (Exception cleanup) when (!CqrsBoundaryErrors.IsNonFatal(cleanup))
                {
                    storageFailures.Add(cleanup);
                }
                KeyLoad.Server.ServerFailureObserver.ThrowIfAny(storageFailures);
            }, failures);
        }
        catch (Exception cleanup) when (CqrsBoundaryErrors.IsNonFatal(cleanup))
        {
            failures.Add(cleanup);
        }
        catch (Exception cleanup) when (!CqrsBoundaryErrors.IsNonFatal(cleanup))
        {
            failures.Add(cleanup);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RequireRegisteredAsync(EventFeedPubSubStorage storage,
        IOptions<GrainRoutingOptions> routing, CancellationToken token)
    {
        var id = EventFeedPubSubFreshStateFixture.IdFor("/scratch-refusal");
        var original = new GrainState<List<string>>([Original]);
        await storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, id, original, token);
        var originalTag = original.ETag;
        var refused = new GrainState<List<string>>([new string('x', routing.Value.MaximumScratchBytes * 16)])
        {
            ETag = originalTag,
            RecordExists = true
        };
        KeyLoadException? failure = null;
        try
        {
            await storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, id, refused, token);
        }
        catch (KeyLoadException actual)
        {
            failure = actual;
        }
        await Assert.That(failure).IsNotNull();
        await RequireCapacity(failure!);
        var read = new GrainState<List<string>>();
        await storage.ReadStateAsync(EventFeedPubSubProtocol.StateName, id, read, token);
        await RequireStored(read, Original, originalTag);
        await Assert.That(refused.ETag).IsEqualTo(originalTag);
        await Assert.That(refused.RecordExists).IsTrue();
        var healthy = new GrainState<List<string>>([Healthy]) { ETag = originalTag, RecordExists = true };
        await storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, id, healthy, token);
        await storage.ReadStateAsync(EventFeedPubSubProtocol.StateName, id, read, token);
        await RequireStored(read, Healthy, healthy.ETag);
        var retainedHealthy = healthy.State;
        await storage.ClearStateAsync(EventFeedPubSubProtocol.StateName, id, healthy, token);
        await Assert.That(ReferenceEquals(healthy.State, retainedHealthy)).IsFalse();
        await Assert.That(EventFeedPubSubFreshStateFixture.RequireState(healthy).Count).IsEqualTo(0);
        await Assert.That(healthy.ETag).IsNull();
        await Assert.That(healthy.RecordExists).IsFalse();
        var absent = new GrainState<List<string>>();
        await storage.ReadStateAsync(EventFeedPubSubProtocol.StateName, id, absent, token);
        await Assert.That(EventFeedPubSubFreshStateFixture.RequireState(absent).Count).IsEqualTo(0);
        await Assert.That(absent.RecordExists).IsFalse();
        await Assert.That(absent.ETag).IsNull();
        var fresh = new GrainState<List<string>>([Original]);
        await storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, id, fresh, token);
        await storage.ReadStateAsync(EventFeedPubSubProtocol.StateName, id, absent, token);
        await RequireStored(absent, Original, fresh.ETag);
    }

    private static IOptions<GrainRoutingOptions> Routing(int maximum = ScratchBytes)
        => Options.Create(new GrainRoutingOptions { MinimumScratchBytes = ScratchBytes, MaximumScratchBytes = maximum });

    private static void Request(EventFeedPubSubCountingWriter writer, bool memory, int hint)
    {
        if (memory)
        {
            _ = writer.GetMemory(hint);
        }
        else
        {
            _ = writer.GetSpan(hint);
        }
    }

    private static async Task RequireCapacity(KeyLoadException failure)
    {
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(failure.Message).IsEqualTo(EventFeedPubSubProtocol.Capacity);
        await Assert.That(failure.InnerException is KeyLoadException).IsTrue();
        await Assert.That(((KeyLoadException)failure.InnerException!).Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    private static async Task RequireStored(GrainState<List<string>> state, string value, string? tag)
    {
        await Assert.That(state.State).IsEquivalentTo(new[] { value }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(state.ETag).IsEqualTo(tag);
        await Assert.That(state.RecordExists).IsTrue();
    }
}
