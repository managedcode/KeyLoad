using KeyLoad.ComparisonHost.Features.BenchmarkComparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Owns actual buffered-writer/configuration lifetimes for disposal-policy tests, without database or provenance simulation.</summary>
internal sealed class SessionDisposalNativeFixture : IAsyncDisposable
{
    private NativeWriter? owner;
    private ConfigurationManager? configuration;
    private NativeSource? source;
    private Func<int>? ownerDisposals;
    private Func<Exception?>? ownerFailure;

    internal int OwnerDisposals => ownerDisposals?.Invoke() ?? 0;
    internal int ConfigurationDisposals => source?.Disposals ?? 0;
    internal Exception? OwnerFailure => ownerFailure?.Invoke();
    internal Exception? ConfigurationFailure => source?.Failure;

    internal static async Task<SessionDisposalNativeFixture> CreateAsync(bool nativeFailure, bool configurationFailure)
    {
        var fixture = new SessionDisposalNativeFixture();
        try
        {
            fixture.Open();
            await fixture.owner!.ArmAsync(nativeFailure);
            await fixture.source!.ArmAsync(configurationFailure);
            return fixture;
        }
        catch (Exception original)
        {
            await fixture.CompleteAsync(Task.FromException(original));
            throw;
        }
    }

    private void Open()
    {
        owner = new NativeWriter();
        var observedOwner = owner;
        ownerDisposals = () => observedOwner.AsyncDisposals;
        ownerFailure = () => observedOwner.Failure;
        configuration = new ConfigurationManager();
        source = new NativeSource();
        ((IConfigurationBuilder)configuration).Add(source);
    }

    internal ValueTask CompleteAsync(Task original)
    {
        var owned = owner;
        var lifetime = configuration;
        owner = null;
        configuration = null;
        return BenchmarkSessionFixtureDisposal.CompleteAsync(original, owned, lifetime);
    }

    public ValueTask DisposeAsync() => CompleteAsync(Task.CompletedTask);

    private sealed class NativeSource : IConfigurationSource
    {
        private Func<int>? disposals;
        private Func<Exception?>? failure;
        private Func<bool, Task>? arm;
        internal int Disposals => disposals?.Invoke() ?? 0;
        internal Exception? Failure => failure?.Invoke();
        internal Task ArmAsync(bool fail) => arm!(fail);
        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            var provider = new NativeProvider();
            disposals = () => provider.Disposals;
            failure = () => provider.Failure;
            arm = provider.ArmAsync;
            return provider;
        }
    }

    private sealed class NativeProvider : ConfigurationProvider, IDisposable
    {
        private readonly NativeWriter writer = new();
        internal int Disposals => writer.SyncDisposals;
        internal Exception? Failure => writer.Failure;
        internal Task ArmAsync(bool fail) => writer.ArmAsync(fail);
        public void Dispose() => writer.Dispose();
    }

    private sealed class NativeWriter : IAsyncDisposable, IDisposable
    {
        private const string Marker = "original-buffered-native-disposal";
        private readonly StreamWriter writer = new(new MemoryStream());
        internal int AsyncDisposals { get; private set; }
        internal int SyncDisposals { get; private set; }
        internal Exception? Failure { get; private set; }

        internal async Task ArmAsync(bool fail)
        {
            await writer.WriteAsync(Marker);
            if (fail)
            {
                // Actual BCL flush/disposal observes its already closed underlying resource.
                writer.BaseStream.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            AsyncDisposals++;
            try
            {
                await writer.DisposeAsync();
            }
            catch (Exception original)
            {
                Failure = original;
                throw;
            }
            finally
            {
                GC.SuppressFinalize(this);
            }
        }

        public void Dispose()
        {
            SyncDisposals++;
            try
            {
                writer.Dispose();
            }
            catch (Exception original)
            {
                Failure = original;
                throw;
            }
            finally
            {
                GC.SuppressFinalize(this);
            }
        }
    }
}
