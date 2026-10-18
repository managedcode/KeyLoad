using System.Security.Cryptography;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Server;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class PartitionHostRecoveryFixture : IDisposable
{
    internal const string CanonicalDirectory = "database";
    internal const string OwnershipFile = "node.owner.lock";
    private const string Prefix = "keyload-host-recovery-";
    private const string TargetDirectory = "target";
    private const string GuidFormat = "N";
    private const string AdminPrefix = "root.";
    private const int SecretBytes = 32;
    private const int NoFailures = 0;
    private const string UncertainPriorOwner = "A prior native owner attempt is uncertain; retain its root.";
    private readonly List<PartitionHostRecoveryAttempt> attempts = [];
    private bool retainRoot;
    private readonly string directory;
    private readonly ILoggerFactory logging = LoggerFactory.Create(_ =>
    {
    });

    internal PartitionHostRecoveryFixture()
    {
        directory = Path.Combine(StorageTrialDirectory.ResolveExistingDirectory(new(Path.GetTempPath())),
            Prefix + Guid.NewGuid().ToString(GuidFormat));
        Options = new()
        {
            DataDirectory = Path.Combine(directory, TargetDirectory),
            Incarnation = Guid.NewGuid(),
            SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes)),
            AdminKey = AdminPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(SecretBytes))
        };
        Options.Validate();
    }

    internal NodeOptions Options
    {
        get;
    }

    internal void RetainRoot() => retainRoot = true;

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

    internal async Task RunHostAsync(Func<PartitionHost, Task> body)
    {
        var attempt = CreateAttempt();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var lease = new PartitionHostRecoveryLease(attempt);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await lease.OpenAsync(Options, logging.CreateLogger<PartitionHost>()).ConfigureAwait(false);
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

    public void Dispose()
    {
        var failures = new List<Exception>();
        foreach (var attempt in attempts)
        {
            ServerFailureObserver.Observe(attempt.RequireReleased, failures);
        }
        ServerFailureObserver.Observe(logging.Dispose, failures);
        if (!retainRoot && failures.Count == NoFailures && Directory.Exists(directory))
        {
            ServerFailureObserver.Observe(() => Directory.Delete(directory, true), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
