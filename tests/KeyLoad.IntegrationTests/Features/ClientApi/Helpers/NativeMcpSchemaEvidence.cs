using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.GraphTraversal;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>Retains only the bounded native schemas needed to diagnose official-client acceptance failures.</summary>
internal static class NativeMcpSchemaEvidence
{
    private const int MaximumSchemaBytes = 64 * 1_024;
    private const int MaximumSidecarBytes = 1_024;
    private const int WriteBufferBytes = 4_096;
    private const string SolutionFileName = "KeyLoad.slnx";
    private const string ArtifactDirectory = "artifacts";
    private const string QualificationDirectory = "qualification";
    private const string EvidenceDirectory = "mcp-schema-evidence";
    private const string SchemaSuffix = ".schema.json";
    private const string SidecarSuffix = ".metadata.json";
    private const string CaptureSource =
        "ModelContextProtocol.Client.McpClient.DiscoverKeyLoadToolAsync.Tool.InputSchema";
    private const string CaptureContext = "Aspire-owned Docker RF3 official MCP C# SDK client, node1";
    private static readonly JsonSerializerOptions SidecarOptions = new(JsonSerializerDefaults.Web);

    internal static Task RetainGraphShortestPathAsync(Tool tool, CancellationToken token)
        => RetainInputSchemaAsync(tool, McpCallerProtocol.GraphShortestPath, token);

    internal static Task RetainIncomingGraphAsync(Tool tool, CancellationToken token)
        => RetainInputSchemaAsync(tool, GraphIncomingMcpProtocol.Tool, token);

    internal static Task RetainPartitionQueryAsync(Tool tool, CancellationToken token)
        => RetainInputSchemaAsync(tool, McpCallerTools.QueryPartitions, token);

    private static async Task RetainInputSchemaAsync(Tool tool, string expectedName, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var filenamePrefix = FilenamePrefix(expectedName);
        if (!string.Equals(tool.Name, expectedName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
            "The MCP schema evidence tool name did not match its fixed capture scope.");
        }
        if (tool.InputSchema.ValueKind != JsonValueKind.Object)
        { throw new InvalidDataException("The native MCP input schema is not a JSON object."); }
        var schemaText = tool.InputSchema.GetRawText();
        if (schemaText.Length > MaximumSchemaBytes
            || Encoding.UTF8.GetByteCount(schemaText) > MaximumSchemaBytes)
        { throw new InvalidDataException("The native MCP input schema exceeds its evidence bound."); }
        var schemaBytes = Encoding.UTF8.GetBytes(schemaText);

        var repository = ClusterFixtureDiagnostics.FindRepositoryRoot();
        if (!File.Exists(Path.Combine(repository.FullName, SolutionFileName)))
        { throw new InvalidOperationException("The repository root for MCP schema evidence was not found."); }
        var directory = Path.Combine(repository.FullName, ArtifactDirectory, QualificationDirectory, EvidenceDirectory);
        Directory.CreateDirectory(directory);
        var name = filenamePrefix + Guid.NewGuid().ToString("N");
        var schemaFile = name + SchemaSuffix;
        var schemaPath = Path.Combine(directory, schemaFile);
        var metadataPath = Path.Combine(directory, name + SidecarSuffix);
        var metadata = new Sidecar(tool.Name, CaptureSource, CaptureContext, schemaFile, schemaBytes.Length,
            Convert.ToHexStringLower(SHA256.HashData(schemaBytes)));
        var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(metadata, SidecarOptions);
        if (metadataBytes.Length > MaximumSidecarBytes)
        { throw new InvalidDataException("The native MCP schema evidence sidecar exceeds its bound."); }

        await WriteNewAsync(schemaPath, schemaBytes, token).ConfigureAwait(false);
        await WriteNewAsync(metadataPath, metadataBytes, token).ConfigureAwait(false);
    }

    private static string FilenamePrefix(string toolName) => toolName switch
    {
        McpCallerProtocol.GraphShortestPath => "graph-shortest-path-",
        GraphIncomingMcpProtocol.Tool => "graph-incoming-",
        McpCallerTools.QueryPartitions => "partition-query-",
        _ => throw new InvalidOperationException("The MCP schema evidence tool is outside the fixed capture scope.")
    };

    private static async Task WriteNewAsync(string path, byte[] content, CancellationToken token)
    {
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            WriteBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await output.WriteAsync(content, token).ConfigureAwait(false);
        await output.FlushAsync(token).ConfigureAwait(false);
    }

    private sealed record Sidecar(string ToolName, string CaptureSource, string Context, string SchemaFile,
        int SchemaBytes, string Sha256);
}
