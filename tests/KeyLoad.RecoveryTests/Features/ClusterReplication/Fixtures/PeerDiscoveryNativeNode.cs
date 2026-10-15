using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;
namespace KeyLoad.RecoveryTests;

internal sealed class PeerDiscoveryNativeNode : IAsyncDisposable
{
    private readonly ReplicaTermMetadataLifetime journal;
    private readonly ReplicaMaterializer materializer;
    private readonly Lock lifecycle = new();
    private Task? disposal;
    private Task? warmClose;
    private bool finalRequested;
    private bool retainDirectory;
    private bool retainOnFailure;
    internal string EvidenceDirectory
    {
        get;
    }
    private ReplicaTermMetadataLifetime? coldJournal;
    private ReplicaMaterializer? coldMaterializer;
    private readonly PeerDiscoveryNativeClockProfile? clockProfile;
    internal DatabaseEngine Database => coldJournal?.Database ?? journal.Database;
    internal ZoneTreeStore ReplicaStore => coldJournal?.BorrowReplica() ?? journal.BorrowReplica();
    internal DurableReplicaLog Log => coldJournal?.BorrowLog() ?? journal.BorrowLog();
    internal ReplicaMaterializer Materializer => coldMaterializer ?? materializer;
    internal ReplicaConsensus Consensus
    {
        get;
    }
    internal ReplicaConfiguration Configuration => journal.Configuration;
    internal ReplicaPeerOptions Peers
    {
        get;
    }
    internal ReplicaSiloDiscoveryState Discovery
    {
        get;
    }
    internal ReplicaEnvelopeAuthenticator Authentication
    {
        get;
    }
    internal ReplicaNativeDiscoveryRuntime? Runtime
    {
        get; set;
    }

    internal PeerDiscoveryNativeNode(ReplicaConfiguration configuration, ILocalSiloDetails local, ReadOnlyMemory<byte> secret,
        PeerDiscoveryNativeClockProfile? profile = null)
    {
        clockProfile = profile;
        EvidenceDirectory = ReplicaFixturePaths.NewDirectory(PeerDiscoveryNativeFixtureProtocol.DirectoryPrefix);
        ReplicaTermMetadataLifetime? openedJournal = null;
        ReplicaMaterializer? openedMaterializer = null;
        ReplicaConsensus? openedConsensus = null;
        try
        {
            journal = openedJournal = ReplicaTermMetadataLifetime.Open(EvidenceDirectory, null,
                configuration with
                {
                    Directory = Path.Combine(EvidenceDirectory, PeerDiscoveryNativeFixtureProtocol.ReplicaDirectory)
                }, profile?.ClockFor(configuration.LocalId), profile?.InitialSigningKey);
            profile?.Bootstrap(journal.Database, configuration.VoterIds);
            var execution = RecoveryExecutionOptions.Replica();
            openedMaterializer = new(journal.Database, journal.BorrowLog(), new ReplicaSnapshotStore(
                journal.Database.Store, journal.BorrowLog(), RecoveryExecutionOptions.Configuration(Configuration), execution), execution);
            materializer = openedMaterializer;
            openedConsensus = new(materializer, RecoveryExecutionOptions.Configuration(Configuration), execution, TimeProvider.System);
            Consensus = openedConsensus;
            Peers = new(new(StringComparer.Ordinal), secret, PeerDiscoveryNativeFixtureProtocol.Cohort)
            {
                ConnectTimeout = PeerDiscoveryNativeFixtureProtocol.ConnectTimeout
            };
            var configured = RecoveryExecutionOptions.Configuration(Configuration);
            var peers = Options.Create(Peers);
            Discovery = new(configured, peers, local, StoreReaderContract.RuntimeJournal);
            Authentication = new(configured, peers, Discovery, TimeProvider.System,
                Options.Create(new ReplicaTransportOptions()), Options.Create(Peers.ReplayLimits), canonicalDatabase: journal.Database);
        }
        catch (Exception primary)
        {
            PeerDiscoveryNativeColdShutdown.CloseFailedConstruction(primary, openedConsensus,
                openedMaterializer, openedJournal, EvidenceDirectory);
            throw;
        }
    }

    internal void RetainDirectoryForCold() => retainDirectory = true;

    internal void RetainOnFailure() => retainOnFailure = true;

    internal void ReopenAfterJoinedCold()
    {
        if (!retainDirectory || finalRequested || warmClose is not { IsCompletedSuccessfully: true } || coldJournal is not null)
        {
            throw new InvalidOperationException(PeerDiscoveryNativeClockProfile.ColdBeforeJoined);
        }
        coldJournal = ReplicaTermMetadataLifetime.Open(EvidenceDirectory, null, journal.Configuration,
            clockProfile?.ClockFor(Configuration.LocalId), deleteDirectoryOnFailure: false);
        var execution = RecoveryExecutionOptions.Replica();
        coldMaterializer = new(coldJournal.Database, coldJournal.BorrowLog(), new ReplicaSnapshotStore(
            coldJournal.Database.Store, coldJournal.BorrowLog(), RecoveryExecutionOptions.Configuration(Configuration), execution), execution);
    }

    internal Task CloseForColdAsync()
    {
        lock (lifecycle)
        {
            return warmClose ??= CloseWarmAsync();
        }
    }

    internal Task DisposeColdAndDeleteAsync()
    {
        lock (lifecycle)
        {
            finalRequested = true;
            return disposal ??= DisposeCoreAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            if (retainDirectory && coldJournal is null && !finalRequested)
            {
                return new(warmClose ??= CloseWarmAsync());
            }
            finalRequested = true;
            return new(disposal ??= DisposeCoreAsync());
        }
    }

    private async Task CloseWarmAsync()
    {
        List<Exception> failures = [];
        if (Runtime is { } runtime)
        {
            await ReplicaMaterializerLifecycleErrors.AttemptAsync(runtime.StopAsync, failures);
        }
        await ReplicaMaterializerLifecycleErrors.AttemptAsync(() => Consensus.DisposeAsync().AsTask(), failures);
        try
        {
            await materializer.DisposeAsync();
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        try
        {
            journal.Dispose();
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        ReplicaMaterializerLifecycleErrors.Attempt(Authentication.Dispose, failures);
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }

    private async Task DisposeCoreAsync()
    {
        List<Exception> failures = [];
        await ReplicaMaterializerLifecycleErrors.AttemptAsync(CloseForColdAsync, failures);
        if (coldMaterializer is not null)
        {
            try
            {
                await coldMaterializer.DisposeAsync();
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                ReplicaMaterializerLifecycleErrors.Add(failure, failures);
            }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                ReplicaMaterializerLifecycleErrors.Add(failure, failures);
            }
        }
        if (coldJournal is not null)
        {
            try
            {
                coldJournal.Dispose();
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                ReplicaMaterializerLifecycleErrors.Add(failure, failures);
            }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                ReplicaMaterializerLifecycleErrors.Add(failure, failures);
            }
        }
        if (!retainOnFailure && failures.Count == PeerDiscoveryNativeFixtureProtocol.NoFailures && Directory.Exists(EvidenceDirectory))
        {
            ReplicaMaterializerLifecycleErrors.Attempt(() => Directory.Delete(EvidenceDirectory, true), failures);
        }
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }
}
