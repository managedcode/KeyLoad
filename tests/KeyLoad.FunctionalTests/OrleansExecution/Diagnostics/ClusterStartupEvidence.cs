using System.Runtime.ExceptionServices;
using System.Text.Json;
using Orleans.TestingHost;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class ClusterStartupEvidence
{
    internal static string PathFor(string clusterId)
    {
        if (!ClusterStartupProtocol.Identifier(clusterId))
        {
            throw new IOException("The execution startup owner identity is invalid.");
        }
        return Path.Combine(TUnit.Core.TestContext.ResultsDirectory,
            ClusterStartupProtocol.DirectoryName, clusterId + ".json");
    }

    internal static string OperationPathFor(string clusterId)
        => PathFor(clusterId + ClusterStartupProtocol.OperationSuffix);

    internal static void SaveOperationFailure(TestCluster cluster, string owner, string logRoot,
        bool joined, IReadOnlyList<Exception> failures)
    {
        var trace = new ClusterStartupTrace();
        var files = joined ? ReadOwnedFiles(cluster, logRoot, trace) : [];
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = ClusterStartupProtocol.SchemaVersion,
            kind = ClusterStartupProtocol.OperationKind,
            clusterId = cluster.Options.ClusterId,
            owner,
            cleanupJoined = joined,
            exceptionTypes = failures.SelectMany(ExceptionTypes).ToArray(),
            fileLogging = cluster.Options.ConfigureFileLogging,
            runtimeExpectedFiles = cluster.Options.InitialSilosCount + 1,
            missingFiles = cluster.Options.InitialSilosCount + 1 - files.Count,
            files,
            trace.Saturated,
            events = trace.Events
        });
        Write(OperationPathFor(cluster.Options.ClusterId), bytes);
    }

    internal static async Task ThrowAfterCleanupAsync(Exception original, TestCluster cluster,
        string owner, string logRoot, bool callerCancelled, bool deadlineCancelled, Func<Task> cleanup)
    {
        var failures = new List<Exception> { original };
        var joined = false;
        try
        {
            await cleanup();
            joined = true;
        }
        catch (Exception cleanupFailure) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanupFailure)) { failures.Add(cleanupFailure); }
        catch (Exception cleanupFailure) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanupFailure)) { failures.Add(cleanupFailure); }
        try
        {
            Save(cluster, owner, logRoot, callerCancelled, deadlineCancelled, joined, failures);
        }
        catch (Exception captureFailure) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(captureFailure)) { failures.Add(captureFailure); }
        catch (Exception captureFailure) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(captureFailure)) { failures.Add(captureFailure); }
        if (failures.Count != 1)
        {
            throw new AggregateException(failures);
        }
        ExceptionDispatchInfo.Capture(original).Throw();
    }

    private static void Save(TestCluster cluster, string owner, string logRoot, bool callerCancelled,
        bool deadlineCancelled, bool joined, IReadOnlyList<Exception> failures)
    {
        var path = PathFor(cluster.Options.ClusterId);
        var trace = new ClusterStartupTrace();
        var files = joined ? ReadOwnedFiles(cluster, logRoot, trace) : [];
        var types = failures.SelectMany(ExceptionTypes).ToArray();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = ClusterStartupProtocol.SchemaVersion,
            kind = ClusterStartupProtocol.Kind,
            clusterId = cluster.Options.ClusterId,
            owner,
            callerCancelled,
            deadlineCancelled,
            cleanupJoined = joined,
            exceptionTypes = types,
            fileLogging = cluster.Options.ConfigureFileLogging,
            runtimeExpectedFiles = cluster.Options.InitialSilosCount + 1,
            missingFiles = cluster.Options.InitialSilosCount + 1 - files.Count,
            files,
            trace.Saturated,
            events = trace.Events
        });
        Write(path, bytes);
    }

    internal static void Write(string path, byte[] bytes)
    {
        if (bytes.Length > ClusterStartupProtocol.MaximumOutputBytes)
        {
            throw new IOException("The execution startup evidence exceeded its bound.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)
            ?? throw new IOException("The execution startup evidence has no owning directory."));
        var saveFailures = new List<Exception>();
        try
        {
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            KeyLoad.Server.ServerFailureObserver.Observe(() =>
            {
                output.Write(bytes);
                output.Flush();
            }, saveFailures);
        }
        catch (Exception saveOrCloseFailure) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(saveOrCloseFailure)) { saveFailures.Add(saveOrCloseFailure); }
        catch (Exception saveOrCloseFailure) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(saveOrCloseFailure)) { saveFailures.Add(saveOrCloseFailure); }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(saveFailures);
    }

    private static List<ClusterStartupFile> ReadOwnedFiles(TestCluster cluster, string root,
        ClusterStartupTrace trace)
    {
        var files = new List<ClusterStartupFile>();
        if (!Directory.Exists(root))
        {
            return files;
        }
        foreach (var path in Directory.EnumerateFiles(root, cluster.Options.ClusterId + "_*.log")
            .Take(cluster.Options.InitialSilosCount + 2))
        {
            if (files.Count == cluster.Options.InitialSilosCount + 1)
            {
                throw new IOException("The execution startup owner has unexpected trace files.");
            }
            var name = Path.GetFileNameWithoutExtension(path);
            var suffix = name[(cluster.Options.ClusterId.Length + 1)..];
            var separator = suffix.IndexOf('_', StringComparison.Ordinal);
            if (separator < 1 || !suffix[..separator].All(static digit => char.IsAsciiHexDigit(digit)))
            {
                throw new IOException("The execution startup trace identity is invalid.");
            }
            var role = suffix[(separator + 1)..];
            var secondary = role.StartsWith("Secondary_", StringComparison.Ordinal)
                && int.TryParse(role["Secondary_".Length..], out var index)
                && index >= 0 && index < cluster.Options.InitialSilosCount;
            if (role is not ("Primary" or "MainClient") && !secondary
                || files.Any(file => file.Role == role))
            {
                throw new IOException("The execution startup trace role is invalid.");
            }
            files.Add(trace.Read(path, role));
        }
        return files;
    }

    internal static IEnumerable<string> ExceptionTypes(Exception failure)
    {
        var current = failure;
        for (var depth = 0; depth < ClusterStartupProtocol.MaximumExceptionDepth; depth++)
        {
            yield return current.GetType().FullName ?? nameof(Exception);
            if (current.InnerException is not { } inner)
            {
                yield break;
            }
            current = inner;
        }
    }
}
