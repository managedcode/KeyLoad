using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Serialization;
using Orleans.Serialization.Serializers;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class EventFeedPubSubFreshStateFixture : IDisposable
{
    internal const string Original = "original-execution-subscription";
    internal const string Healthy = "healthy-execution-subscription";
    private const string Flow = "/fresh-state";
    private const int MinimumScratchBytes = 256;
    private const int MaximumScratchBytes = 4096;
    private const int MaximumRows = 1;
    private readonly ServiceProvider services;

    internal EventFeedPubSubFreshStateFixture()
    {
        ServiceProvider? acquiredServices = null;
        EventFeedPubSubStorage? acquiredStorage = null;
        try
        {
            var registrations = new ServiceCollection();
            registrations.AddSerializer(builder => builder.AddAssembly(typeof(EventFeedWakeupHint).Assembly));
            var actualServices = registrations.BuildServiceProvider();
            acquiredServices = actualServices;
            var routing = Options.Create(new GrainRoutingOptions
            {
                MinimumScratchBytes = MinimumScratchBytes,
                MaximumScratchBytes = MaximumScratchBytes
            });
            var limits = Options.Create(new DatabaseLimits { MaxResults = MaximumRows });
            var actualStorage = new EventFeedPubSubStorage(actualServices, limits, routing);
            acquiredStorage = actualStorage;
            var actualId = IdFor(Flow);
            services = actualServices;
            Storage = actualStorage;
            Id = actualId;
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            FailHandoff(error);
            throw;
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            FailHandoff(error);
            throw;
        }

        void FailHandoff(Exception original)
        {
            var failures = new List<Exception> { original };
            if (acquiredStorage is not null)
            {
                ServerFailureObserver.Observe(acquiredStorage.Dispose, failures);
            }
            if (acquiredServices is not null)
            {
                ServerFailureObserver.Observe(acquiredServices.Dispose, failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    internal static async Task RunAsync(Func<EventFeedPubSubFreshStateFixture, Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var failures = new List<Exception>();
        try
        {
            using var fixture = new EventFeedPubSubFreshStateFixture();
            await ServerFailureObserver.ObserveAsync(() => body(fixture), failures).ConfigureAwait(false);
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal EventFeedPubSubStorage Storage
    {
        get;
    }
    internal GrainId Id
    {
        get;
    }

    internal T RuntimeCreateState<T>()
        => services.GetRequiredService<IActivatorProvider>().GetActivator<T>().Create();

    internal static GrainState<List<string>> OriginalState() => new([Original]);

    internal static GrainId IdFor(string suffix)
    {
        var id = GrainId.Create(EventFeedPubSubProtocol.GrainTypeName, EventFeedPubSubProtocol.ProviderName + suffix);
        EventFeedPubSubIdentity.Require(EventFeedPubSubProtocol.StateName, id);
        return id;
    }

    internal static List<string> RequireState(GrainState<List<string>> state)
        => state.State ?? throw new InvalidOperationException("Execution subscription state is missing.");

    internal async Task RequireFreshAsync(GrainState<List<string>> state, List<string> prior)
    {
        var execution = services.GetRequiredService<IActivatorProvider>().GetActivator<List<string>>().Create();
        await Assert.That(ReferenceEquals(state.State, prior)).IsFalse();
        await Assert.That(RequireState(state).SequenceEqual(execution)).IsTrue();
        await Assert.That(state.ETag).IsNull();
        await Assert.That(state.RecordExists).IsFalse();
    }

    internal async Task RequireStoredAsync(string value, string? tag, CancellationToken token)
    {
        var actual = new GrainState<List<string>>();
        await Storage.ReadStateAsync(EventFeedPubSubProtocol.StateName, Id, actual, token);
        await Assert.That(RequireState(actual).Count).IsEqualTo(MaximumRows);
        await Assert.That(RequireState(actual).Single()).IsEqualTo(value);
        await Assert.That(actual.ETag).IsEqualTo(tag);
        await Assert.That(actual.RecordExists).IsTrue();
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(Storage.Dispose, failures);
        try
        {
            services.Dispose();
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
