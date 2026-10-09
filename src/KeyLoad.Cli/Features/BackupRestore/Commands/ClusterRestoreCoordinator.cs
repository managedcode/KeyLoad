using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Owns one all-groups unpublished offline restore; it never starts or bypasses an RF3 host.</summary>
internal static class ClusterRestoreCoordinator
{
    private const string Invalid = "The configured cluster restore vector or node binding is invalid.";
    private const int RequiredVoters = 3;
    private const int MaximumSigningKeyCharacters = 44;

    internal static ClusterRestoreExecutionReceipt Run(IOptions<ClusterRestoreOperatorConfiguration> configuration,
        IOptions<ZoneTreeStorageExecutionOptions> storage, IOptions<DatabaseLimits> limits,
        TimeProvider clock, CancellationToken cancellationToken)
        => Run(configuration, storage, limits, clock, null, cancellationToken);

    internal static ClusterRestoreExecutionReceipt Run(IOptions<ClusterRestoreOperatorConfiguration> configuration,
        IOptions<ZoneTreeStorageExecutionOptions> storage, IOptions<DatabaseLimits> limits,
        TimeProvider clock, Action<NativeClusterRestoreStage>? observer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(clock);
        var options = configuration.Value;
        var started = clock.GetTimestamp();
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.DestinationRoot));
        if (destination == Path.GetPathRoot(destination) || File.Exists(destination))
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        var work = new ReadExecutionBudget(limits, clock, cancellationToken);
        var source = ClusterRestoreSourceReader.Read(options, storage, limits, clock, work, cancellationToken);
        var operationRoot = ClusterRestoreDestinationOwner.OperationRoot(destination, options.OperationId);
        var owner = ClusterRestoreDestinationOwner.Acquire(destination, storage);
        Exception? primary = null;
        var cleanupCompleted = false;
        try
        {
            try
            {
                return RunAdmitted(configuration, options, storage, limits, clock, work, source,
                    operationRoot, destination, started, observer, cancellationToken);
            }
            catch (Exception failure) { primary = failure; throw; }
            finally { owner.Dispose(); cleanupCompleted = true; }
        }
        catch (Exception terminal)
        {
            if (primary is not null && !cleanupCompleted)
            { throw new AggregateException(primary, terminal); }
            throw;
        }
    }

    private static ClusterRestoreExecutionReceipt RunAdmitted(IOptions<ClusterRestoreOperatorConfiguration> configuration,
        ClusterRestoreOperatorConfiguration options, IOptions<ZoneTreeStorageExecutionOptions> storage,
        IOptions<DatabaseLimits> limits, TimeProvider clock, ReadExecutionBudget work, ClusterRestoreSourceReader.Result source,
        string operationRoot, string destination, long started, Action<NativeClusterRestoreStage>? observer,
        CancellationToken cancellationToken)
    {
        var previous = ClusterRestoreOriginalPlan.Read(operationRoot, storage.Value);
        if (previous is null)
        { _ = ClusterRestorePathValidation.Root(destination); }
        var signing = ReadSigningIdentities(options, options.Mappings.Select(value => value.ToNative()).ToImmutableArray());
        try
        {
            var candidate = ClusterRestorePlanBuilder.Build(options, destination, source, signing,
                storage, limits, clock, previous?.Value);
            var original = ClusterRestoreOriginalPlan.Admit(operationRoot, candidate, storage.Value, previous, observer);
            var runtime = new ClusterRestoreOperationRuntime(configuration, storage, limits, clock, work,
                original.Value, original.Digest, signing, cancellationToken, observer);
            return ClusterRestoreResumeNodes.Run(runtime, operationRoot, started);
        }
        finally { foreach (var key in signing.Values) { CryptographicOperations.ZeroMemory(key); } }
    }

    internal static void RequireNodes(string root, ClusterRestoreOperatorConfiguration options,
        ImmutableArray<ClusterRestoreOwnerMapping> mappings)
    {
        if (options.Nodes.Length != checked(mappings.Length * RequiredVoters))
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        var paths = new List<string>();
        foreach (var mapping in mappings)
        {
            var nodes = options.Nodes.Where(node => node.SourceOwnerId == mapping.Source.PhysicalShardId).ToArray();
            if (nodes.Length != RequiredVoters
                || !nodes.Select(node => node.VoterId).Order(StringComparer.Ordinal)
                    .SequenceEqual(mapping.Target.VoterIds.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            { throw Errors.Fail(ErrorCode.Validation, Invalid); }
            foreach (var node in nodes)
            { paths.Add(ClusterRestorePathValidation.Relative(root, node.RelativeDataDirectory)); }
        }
        ClusterRestorePathValidation.RequireDistinct(paths);
    }

    internal static Dictionary<Guid, byte[]> ReadSigningIdentities(ClusterRestoreOperatorConfiguration options,
        ImmutableArray<ClusterRestoreOwnerMapping> mappings)
    {
        if (options.SigningIdentities.Length != mappings.Length)
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        var keys = new Dictionary<Guid, byte[]>();
        try
        {
            foreach (var mapping in mappings)
            {
                var candidates = options.SigningIdentities.Where(value => value.SourceOwnerId == mapping.Source.PhysicalShardId).ToArray();
                if (candidates.Length != SingletonCount || candidates.First().SigningKey.Length > MaximumSigningKeyCharacters)
                { throw Errors.Fail(ErrorCode.Validation, Invalid); }
                byte[] decoded;
                try
                { decoded = Convert.FromBase64String(candidates.First().SigningKey); }
                catch (FormatException) { throw Errors.Fail(ErrorCode.Validation, Invalid); }
                keys.Add(mapping.Source.PhysicalShardId, decoded);
            }
            return keys;
        }
        catch (Exception)
        {
            foreach (var key in keys.Values)
            { CryptographicOperations.ZeroMemory(key); }
            throw;
        }
    }

    internal static void RequireCredentials(ClusterRestoreOperatorConfiguration options,
        ImmutableArray<ClusterRestoreOwnerMapping> mappings)
    {
        if (options.Credentials.Length != mappings.Length)
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        foreach (var mapping in mappings)
        {
            var candidates = options.Credentials.Where(value => value.SourceOwnerId == mapping.Source.PhysicalShardId).ToArray();
            if (candidates.Length != SingletonCount || string.IsNullOrWhiteSpace(candidates.First().Credential))
            { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        }
    }

    private const int SingletonCount = 1;

}
