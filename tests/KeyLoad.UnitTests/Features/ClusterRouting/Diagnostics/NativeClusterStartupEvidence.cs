using System.Runtime.ExceptionServices;
using System.Text.Json;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeClusterStartupEvidence
{
    internal static string PathFor(string clusterId)
    {
        if (!NativeClusterStartupProtocol.Identifier(clusterId))
        { throw new IOException("The native startup owner identity is invalid."); }
        return Path.Combine(TUnit.Core.TestContext.ResultsDirectory,
            NativeClusterStartupProtocol.DirectoryName, clusterId + ".json");
    }

    internal static async Task ThrowAfterCleanupAsync(Exception original, TestCluster cluster,
        string owner, string logRoot, bool callerCancelled, bool deadlineCancelled, Func<Task> cleanup)
    {
        var failures = new List<Exception> { original };
        var joined = false;
        try
        { await cleanup(); joined = true; }
        catch (Exception cleanupFailure) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanupFailure)) { failures.Add(cleanupFailure); }
        catch (Exception cleanupFailure) when (!global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanupFailure)) { failures.Add(cleanupFailure); }
        try
        { Save(cluster, owner, logRoot, callerCancelled, deadlineCancelled, joined, failures); }
        catch (Exception captureFailure) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(captureFailure)) { failures.Add(captureFailure); }
        catch (Exception captureFailure) when (!global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(captureFailure)) { failures.Add(captureFailure); }
        if (failures.Count != 1)
        { throw new AggregateException(failures); }
        ExceptionDispatchInfo.Capture(original).Throw();
    }

    private static void Save(TestCluster cluster, string owner, string logRoot, bool callerCancelled,
        bool deadlineCancelled, bool joined, IReadOnlyList<Exception> failures)
    {
        var path = PathFor(cluster.Options.ClusterId);
        var trace = new NativeClusterStartupTrace();
        var files = joined ? ReadOwnedFiles(cluster, logRoot, trace) : [];
        var types = failures.SelectMany(ExceptionTypes).ToArray();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = NativeClusterStartupProtocol.SchemaVersion,
            kind = NativeClusterStartupProtocol.Kind,
            clusterId = cluster.Options.ClusterId,
            owner,
            callerCancelled,
            deadlineCancelled,
            cleanupJoined = joined,
            exceptionTypes = types,
            nativeFileLogging = cluster.Options.ConfigureFileLogging,
            expectedNativeFiles = cluster.Options.InitialSilosCount + 1,
            missingNativeFiles = cluster.Options.InitialSilosCount + 1 - files.Count,
            files,
            trace.Saturated,
            events = trace.Events
        });
        if (bytes.Length > NativeClusterStartupProtocol.MaximumOutputBytes)
        { throw new IOException("The native startup evidence exceeded its bound."); }
        Directory.CreateDirectory(Path.GetDirectoryName(path)
            ?? throw new IOException("The native startup evidence has no owning directory."));
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
        catch (Exception saveOrCloseFailure) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(saveOrCloseFailure)) { saveFailures.Add(saveOrCloseFailure); }
        catch (Exception saveOrCloseFailure) when (!global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(saveOrCloseFailure)) { saveFailures.Add(saveOrCloseFailure); }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(saveFailures);
    }

    private static List<NativeClusterStartupFile> ReadOwnedFiles(TestCluster cluster, string root,
        NativeClusterStartupTrace trace)
    {
        var files = new List<NativeClusterStartupFile>();
        if (!Directory.Exists(root))
        { return files; }
        foreach (var path in Directory.EnumerateFiles(root, cluster.Options.ClusterId + "_*.log")
            .Take(cluster.Options.InitialSilosCount + 2))
        {
            if (files.Count == cluster.Options.InitialSilosCount + 1)
            { throw new IOException("The native startup owner has unexpected trace files."); }
            var name = Path.GetFileNameWithoutExtension(path);
            var suffix = name[(cluster.Options.ClusterId.Length + 1)..];
            var separator = suffix.IndexOf('_', StringComparison.Ordinal);
            if (separator < 1 || !suffix[..separator].All(static digit => char.IsAsciiHexDigit(digit)))
            { throw new IOException("The native startup trace identity is invalid."); }
            var role = suffix[(separator + 1)..];
            var secondary = role.StartsWith("Secondary_", StringComparison.Ordinal)
                && int.TryParse(role["Secondary_".Length..], out var index)
                && index >= 0 && index < cluster.Options.InitialSilosCount;
            if (role is not ("Primary" or "MainClient") && !secondary
                || files.Any(file => file.Role == role))
            { throw new IOException("The native startup trace role is invalid."); }
            files.Add(trace.Read(path, role));
        }
        return files;
    }

    private static IEnumerable<string> ExceptionTypes(Exception failure)
    {
        var current = failure;
        for (var depth = 0; depth < NativeClusterStartupProtocol.MaximumExceptionDepth; depth++)
        {
            yield return current.GetType().FullName ?? nameof(Exception);
            if (current.InnerException is not { } inner)
            { yield break; }
            current = inner;
        }
    }
}
