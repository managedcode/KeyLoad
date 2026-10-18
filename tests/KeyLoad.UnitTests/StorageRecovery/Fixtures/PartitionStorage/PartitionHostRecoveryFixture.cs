using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Authorization;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class PartitionHostRecoveryFixture : IDisposable
{
    internal const long SnapshotCut = 2;
    internal const long SnapshotTerm = 1;
    internal const string CanonicalDirectory = "database";
    internal const string OwnershipFile = "node.owner.lock";
    private const string Prefix = "keyload-host-recovery-";
    private const string SourceDirectory = "source";
    private const string TargetDirectory = "target";
    private const string GuidFormat = "N";
    private const string AdminPrefix = "root.";
    private const string VoterA = "https://127.0.0.1:5501";
    private const string VoterB = "https://127.0.0.1:5502";
    private const string VoterC = "https://127.0.0.1:5503";
    private const int SecretBytes = 32;
    private const string CaptureImageExceeded = "The complete recovery fixture image exceeded its record bound.";
    private const string CaptureRowSeparator = ":";
    private const string UncertainPriorOwner = "A prior native owner attempt is uncertain; retain its root.";
    private readonly List<PartitionHostRecoveryAttempt> attempts = [];
    private bool retainRoot;
    internal void RetainRoot() => retainRoot = true;
    private readonly string directory;
    private readonly RecoveryImageCaptureOptions capture = UnitExecutionOptions.RecoveryImageCapture().Value;
    private readonly ILoggerFactory logging = LoggerFactory.Create(_ =>
    {
    });

    internal PartitionHostRecoveryFixture()
    {
        directory = Path.Combine(Resolve(new(Path.GetTempPath())), Prefix + Guid.NewGuid().ToString(GuidFormat));
        Options = new()
        {
            DataDirectory = Path.Combine(directory, TargetDirectory),
            PublicEndpoint = VoterA,
            Peers = [VoterA, VoterB, VoterC],
            PhysicalShardId = Guid.NewGuid(),
            Incarnation = Guid.NewGuid(),
            SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes)),
            PeerSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes)),
            AdminKey = AdminPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(SecretBytes))
        };
        Options.Validate();
    }

    internal NodeOptions Options
    {
        get;
    }
    internal ReplicaSnapshot Pending { get; private set; } = null!;
    internal Guid TargetNodeId
    {
        get; private set;
    }
    internal long TargetGeneration
    {
        get; private set;
    }

    internal static async Task RunAsync(Func<PartitionHostRecoveryFixture, Task> body)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new PartitionHostRecoveryFixture();
            await ServerFailureObserver.ObserveAsync(() => body(fixture), failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal void StagePending(PendingPrincipal principal)
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            using var source = OpenStores(Options with
            {
                DataDirectory = Path.Combine(directory, SourceDirectory),
                PublicEndpoint = VoterB
            });
            ServerFailureObserver.Observe(() =>
            {
                using var target = OpenStores();
                ServerFailureObserver.Observe(() => PartitionHostPendingSnapshotStaging.Run(this, source, target, principal), failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal void RecordTarget(HostReplicaStores target)
    {
        TargetNodeId = target.Canonical.Identity.NodeId;
        TargetGeneration = target.Canonical.Identity.ReadGeneration;
    }

    internal void RecordPending(ReplicaSnapshot snapshot) => Pending = snapshot;

    internal async Task RunHostAsync(Func<PartitionHost, Task> body)
    {
        var attempt = CreateAttempt();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var lease = new PartitionHostRecoveryLease(attempt);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await lease.OpenAsync(Options, logging.CreateLogger<ReplicaConsensus>()).ConfigureAwait(false);
                await body(lease.Host).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal async Task RunStoresAsync(Func<PartitionStores, Task> body)
    {
        var attempt = CreateAttempt();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var lease = new PartitionHostRecoveryStoresLease(Options, attempt);
            await ServerFailureObserver.ObserveAsync(() => body(lease.Stores), failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal Task OpenAndDisposeHostAsync() => RunHostAsync(_ => Task.CompletedTask);

    internal Task VerifyRecoveredAuthorityAsync() => RunHostAsync(async host =>
    {
        var position = host.Database.Store.Position;
        var before = Capture(host.Database.Store);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => VerifyAuthority(host.Database));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(host.Database.Store.Position).IsEqualTo(position);
        await Assert.That(Capture(host.Database.Store).SequenceEqual(before, StringComparer.Ordinal)).IsTrue();
    });

    internal static RuntimeJournalMutationResult VerifyAuthority(DatabaseEngine database)
        => NativeJournalAuthorityTestBootstrap.Verify(database);

    private string[] Capture(IAtomicStore store) => store.Read(view =>
    {
        var page = view.Scan([], capture.MaximumRecords);
        if (page.HasMore)
        {
            throw new InvalidOperationException(CaptureImageExceeded);
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + CaptureRowSeparator + Convert.ToHexString(row.Value.Span)).ToArray();
    });

    internal HostReplicaStores OpenStores() => OpenStores(Options);

    private HostReplicaStores OpenStores(NodeOptions options) => new(options, CreateAttempt());

    private PartitionHostRecoveryAttempt CreateAttempt()
    {
        if (attempts.Any(attempt => attempt.IsUncertain))
        {
            throw new InvalidOperationException(UncertainPriorOwner);
        }
        var attempt = new PartitionHostRecoveryAttempt();
        attempts.Add(attempt);
        return attempt;
    }

    internal FileStream AcquireHostOwnership() => new(Path.Combine(Options.DataDirectory, OwnershipFile),
        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static string Resolve(DirectoryInfo directory)
    {
        if (directory.Parent is null)
        {
            return directory.FullName;
        }
        var entry = new DirectoryInfo(Path.Combine(Resolve(directory.Parent), directory.Name));
        return entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? entry.FullName;
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        foreach (var attempt in attempts)
        {
            ServerFailureObserver.Observe(attempt.RequireReleased, failures);
        }
        try
        {
            logging.Dispose();
        }
        catch (Exception failure) when (KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        if (!retainRoot && failures.Count == 0 && Directory.Exists(directory))
        {
            ServerFailureObserver.Observe(() => Directory.Delete(directory, true), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
