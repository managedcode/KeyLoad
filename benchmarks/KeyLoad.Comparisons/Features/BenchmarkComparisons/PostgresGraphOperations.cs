using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Npgsql;

namespace KeyLoad.Comparisons.Targets;

internal static class PostgresGraphOperations
{
    internal static async Task<OperationResult> ExecuteAsync(NpgsqlConnection connection, Scenario scenario,
        BenchmarkDocument document, int graphDepth, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        if (scenario == Scenario.GraphNeighbors)
        {
            command.CommandText = PostgresSchemaCommands.GraphNeighbors;
        }
        else
        {
            command.CommandText = PostgresSchemaCommands.GraphTraverse;
        }
        command.Parameters.AddWithValue(document.Id);
        if (scenario == Scenario.GraphTraverse)
        {
            command.Parameters.AddWithValue(graphDepth);
        }

        return new(Vertices: await ReadVerticesAsync(command, cancellationToken));
    }

    private static async Task<ImmutableArray<string>> ReadVerticesAsync(NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var vertices = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            vertices.Add(reader.GetString(0));
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(vertices.ToArray());
    }
}
