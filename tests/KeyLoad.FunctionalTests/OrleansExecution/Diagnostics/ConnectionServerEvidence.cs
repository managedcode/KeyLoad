using System.Globalization;
using System.Text.Json;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.TestingHost.Logging;

namespace KeyLoad.FunctionalTests.OrleansExecution;

/// <summary>Reads original execution Server logs only after both production host owners have joined.</summary>
internal sealed class ConnectionServerEvidence
{
    private const int FirstNode = 0;
    internal const int RuntimeExpectedFiles = 2;
    private const string NodePrefix = "Node";
    private const string MissingOwner = "The actual execution Server log owner is absent or inconsistent.";
    private readonly string?[] paths = new string?[RuntimeExpectedFiles];
    private string? clusterId;
    internal string ClusterId => clusterId ?? throw new InvalidOperationException(MissingOwner);

    internal void Configure(int index, IServiceCollection services, NodeOptions node)
    {
        var actual = node.ClusterId;
        if (index < FirstNode || index >= paths.Length || paths[index] is not null
            || !ClusterStartupProtocol.Identifier(actual) || clusterId is not null && clusterId != actual)
        {
            throw new InvalidOperationException(MissingOwner);
        }
        clusterId = actual;
        var directory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, actual + "_" + Role(index) + ".log");
        services.AddSingleton<ILoggerProvider>(_ => new FileLoggerProvider(path));
        services.AddLogging(logging => logging.AddFilter<FileLoggerProvider>("Orleans.Runtime.Silo", LogLevel.Information));
        // The execution container creates and owns this provider for its complete host lifetime.
        paths[index] = path;
    }

    internal IReadOnlyList<ClusterStartupFile> ReadJoined(bool joined, ClusterStartupTrace trace)
    {
        if (!joined)
        {
            throw new InvalidOperationException(MissingOwner);
        }
        var files = new List<ClusterStartupFile>();
        for (var index = FirstNode; index < paths.Length; index++)
        {
            var path = paths[index] ?? throw new InvalidOperationException(MissingOwner);
            files.Add(trace.Read(path, Role(index)));
        }
        return files;
    }

    internal void SaveOperationFailure(bool joined, IReadOnlyList<Exception> failures)
    {
        var trace = new ClusterStartupTrace();
        var files = joined ? ReadJoined(joined, trace) : [];
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = ClusterStartupProtocol.SchemaVersion,
            kind = ClusterStartupProtocol.OperationKind,
            clusterId = ClusterId,
            cleanupJoined = joined,
            exceptionTypes = failures.SelectMany(ClusterStartupEvidence.ExceptionTypes).ToArray(),
            fileLogging = paths.All(path => path is not null),
            runtimeExpectedFiles = RuntimeExpectedFiles,
            fileCollectionAttempted = joined,
            missingFiles = joined ? RuntimeExpectedFiles - files.Count : (int?)null,
            files,
            trace.Saturated,
            events = trace.Events
        });
        ClusterStartupEvidence.Write(ClusterStartupEvidence.OperationPathFor(ClusterId), bytes);
    }

    private static string Role(int index) => NodePrefix + index.ToString(CultureInfo.InvariantCulture);
}
