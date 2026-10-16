using System.Globalization;
using System.Text.Json;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.TestingHost.Logging;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Reads original native Server logs only after their actual six host owners have joined.</summary>
internal sealed class ConnectionNativeServerEvidence
{
    private const int FirstNode = 0;
    internal const int ExpectedNativeFiles = 6;
    private const string NodePrefix = "Node";
    private const string MissingOwner = "The actual native Server log owner is absent or inconsistent.";
    private readonly string?[] paths = new string?[ExpectedNativeFiles];
    private string? clusterId;
    internal string ClusterId => clusterId ?? throw new InvalidOperationException(MissingOwner);

    internal void Attach(int index, ControlledPartitionMovementNativeHost host)
    {
        var actual = host.Runtime.Node.Value.ClusterId;
        if (index < FirstNode || index >= paths.Length || paths[index] is not null
            || !NativeClusterStartupProtocol.Identifier(actual) || clusterId is not null && clusterId != actual)
        {
            throw new InvalidOperationException(MissingOwner);
        }
        clusterId = actual;
        var directory = Path.Combine(Directory.GetCurrentDirectory(), "logs");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, actual + "_" + Role(index) + ".log");
        var provider = new FileLoggerProvider(path);
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => host.Application.Services.GetRequiredService<ILoggerFactory>().AddProvider(provider), failures);
        if (failures.Count != FirstNode)
        {
            ServerFailureObserver.Observe(provider.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        // The actual host ILoggerFactory owns and disposes the successfully added provider.
        paths[index] = path;
    }

    internal IReadOnlyList<NativeClusterStartupFile> ReadJoined(bool joined, NativeClusterStartupTrace trace)
    {
        if (!joined)
        {
            throw new InvalidOperationException(MissingOwner);
        }
        var files = new List<NativeClusterStartupFile>();
        for (var index = FirstNode; index < paths.Length; index++)
        {
            var path = paths[index] ?? throw new InvalidOperationException(MissingOwner);
            files.Add(trace.Read(path, Role(index)));
        }
        return files;
    }

    internal void SaveOperationFailure(bool joined, IReadOnlyList<Exception> failures)
    {
        var trace = new NativeClusterStartupTrace();
        var files = joined ? ReadJoined(joined, trace) : [];
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = NativeClusterStartupProtocol.SchemaVersion,
            kind = NativeClusterStartupProtocol.OperationKind,
            clusterId = ClusterId,
            cleanupJoined = joined,
            exceptionTypes = failures.SelectMany(NativeClusterStartupEvidence.ExceptionTypes).ToArray(),
            nativeFileLogging = paths.All(path => path is not null),
            expectedNativeFiles = ExpectedNativeFiles,
            missingNativeFiles = ExpectedNativeFiles - files.Count,
            files,
            trace.Saturated,
            events = trace.Events
        });
        NativeClusterStartupEvidence.Write(NativeClusterStartupEvidence.OperationPathFor(ClusterId), bytes);
    }

    private static string Role(int index) => NodePrefix + index.ToString(CultureInfo.InvariantCulture);
}
