using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Counts canonical metadata bytes without retaining a second serialized catalog.</summary>
internal static class McpGatewayMetadataSizer
{
    private const string NameProperty = "name";
    private const string RouteProperty = "route";
    private const string DescriptionProperty = "description";
    private const string ReadKindProperty = "readKind";
    private const string CommandKindProperty = "commandKind";
    private const string AdapterProperty = "adapter";
    private const string ReadOnlyProperty = "readOnly";
    private const string IdempotentProperty = "idempotent";
    private const string DestructiveProperty = "destructive";
    private const string OpenWorldProperty = "openWorld";
    private const string EnabledProperty = "enabledByDefault";
    private const string InputSchemaProperty = "inputSchema";
    private const string OutputSchemaProperty = "outputSchema";
    private const string HintsProperty = "hints";
    private const string AliasesProperty = "aliases";
    private const string CategoriesProperty = "categories";
    private const string KeywordsProperty = "keywords";
    private const string TagsProperty = "tags";
    private const string DataSourcesProperty = "dataSources";
    private const string UsageExamplesProperty = "usageExamples";
    private const string CostTierProperty = "costTier";
    private const string LatencyTierProperty = "latencyTier";

    internal static void Validate(IReadOnlyList<McpGatewayCatalogEntry> entries, IOptions<McpExecutionOptions> executionOptions)
    {
        using var stream = new BoundedCountingStream(executionOptions.Value.MaximumCatalogMetadataBytes);
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartArray();
        foreach (var entry in entries)
        {
            WriteOperation(writer, entry);
        }

        writer.WriteEndArray();
        writer.Flush();
    }

    private static void WriteOperation(Utf8JsonWriter writer, McpGatewayCatalogEntry entry)
    {
        var operation = entry.Operation;
        writer.WriteStartObject();
        writer.WriteString(NameProperty, operation.Name);
        writer.WriteString(RouteProperty, operation.Route);
        writer.WriteString(DescriptionProperty, operation.Description);
        writer.WriteString(ReadKindProperty, operation.ReadKind?.ToString());
        writer.WriteString(CommandKindProperty, operation.CommandKind?.ToString());
        writer.WriteBoolean(AdapterProperty, operation.IsAdapter);
        writer.WriteBoolean(ReadOnlyProperty, operation.ReadOnly);
        writer.WriteBoolean(IdempotentProperty, operation.Idempotent);
        writer.WriteBoolean(DestructiveProperty, operation.Destructive);
        writer.WriteBoolean(OpenWorldProperty, entry.SearchHints.OpenWorld ?? false);
        writer.WriteBoolean(EnabledProperty, entry.SearchHints.EnabledByDefault ?? true);
        writer.WritePropertyName(InputSchemaProperty);
        operation.InputSchema.WriteTo(writer);
        writer.WritePropertyName(OutputSchemaProperty);
        operation.OutputSchema.WriteTo(writer);
        WriteHints(writer, entry.SearchHints);
        writer.WriteEndObject();
    }

    private static void WriteHints(Utf8JsonWriter writer, ManagedCode.MCPGateway.McpGatewayToolSearchHints hints)
    {
        writer.WriteStartObject(HintsProperty);
        WriteStrings(writer, AliasesProperty, hints.Aliases);
        WriteStrings(writer, KeywordsProperty, hints.Keywords);
        WriteStrings(writer, CategoriesProperty, hints.Categories);
        WriteStrings(writer, TagsProperty, hints.Tags);
        WriteStrings(writer, DataSourcesProperty, hints.DataSources);
        WriteStrings(writer, UsageExamplesProperty, []);
        writer.WriteNull(CostTierProperty);
        writer.WriteNull(LatencyTierProperty);
        writer.WriteEndObject();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IReadOnlyList<string>? values)
    {
        writer.WriteStartArray(name);
        if (values is not null)
        {
            foreach (var value in values)
            {
                writer.WriteStringValue(value);
            }
        }

        writer.WriteEndArray();
    }

    private sealed class BoundedCountingStream(int maximumBytes) : Stream
    {
        private long _written;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _written;
        public override long Position { get => _written; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override void Write(byte[] buffer, int offset, int count) => Count(count);

        public override void Write(ReadOnlySpan<byte> buffer) => Count(buffer.Length);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Count(buffer.Length);
            return ValueTask.CompletedTask;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private void Count(int count)
        {
            if (count > maximumBytes - _written)
            {
                throw new InvalidOperationException(McpGatewayCatalogValidation.MetadataBoundFailure);
            }

            _written += count;
        }
    }
}
