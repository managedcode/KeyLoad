using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using KeyLoad.ComparisonHost.Deployment;

namespace KeyLoad.ComparisonHost.Graph;

/// <summary>
/// Neo4j graph through Cypher on the Query API (<c>POST /db/neo4j/query/v2</c>, one auto-commit transaction per call):
/// a uniqueness constraint on <c>:V(id)</c>, batched <c>UNWIND</c> inserts of <c>:V</c> nodes and <c>:E</c> relationships,
/// a one-hop <c>MATCH</c> for neighbours and <c>count(DISTINCT t)</c> over <c>[:E*1..depth]</c> for the reachable count.
/// </summary>
internal sealed class Neo4jGraphDriver(BenchmarkConnection connection) : IGraphDriver
{
    private const string Constraint = "CREATE CONSTRAINT vertex_id IF NOT EXISTS FOR (v:V) REQUIRE v.id IS UNIQUE";
    private const string AwaitIndexes = "CALL db.awaitIndexes()";
    private const string InsertVertices = "UNWIND $rows AS row CREATE (v:V {id: row.id, number: row.number}) RETURN count(v)";
    private const string InsertEdges = "UNWIND $rows AS row MATCH (a:V {id: row.from}), (b:V {id: row.to}) "
        + "CREATE (a)-[e:E {id: row.id}]->(b) RETURN count(e)";
    private const string Neighbors = "MATCH (s:V {id: $id})-[:E]->(t) RETURN t.id";
    private const string ReachableFrom = "MATCH (s:V {id: $id})-[:E*1..";
    private const string ReachableEnd = "]->(t) WHERE t <> s RETURN count(DISTINCT t)";
    private const string Route = "db/neo4j/query/v2";
    private const string JsonMediaType = "application/json";
    private const string StatementProperty = "statement";
    private const string ParametersProperty = "parameters";
    private const string RowsParameter = "rows";
    private const string IdParameter = "id";
    private const string NumberParameter = "number";
    private const string FromParameter = "from";
    private const string ToParameter = "to";
    private const string DataProperty = "data";
    private const string ValuesProperty = "values";
    private const string ErrorsProperty = "errors";
    private const string Failed = "Neo4j ";
    private const string Separator = ": ";
    private const string WrongCount = "Neo4j created a different number of graph elements than were inserted: ";
    private const string MissingId = "Neo4j returned a neighbour without an id.";
    private const int Batch = 256;
    private static readonly Uri QueryPath = new(Route, UriKind.Relative);
    private readonly HttpClient http = connection.CreateHttpClient(0);

    public int BatchSize => Batch;

    public async Task PrepareAsync(CancellationToken token)
    {
        (await QueryAsync(Constraint, static _ => { }, token)).Dispose();
        (await QueryAsync(AwaitIndexes, static _ => { }, token)).Dispose();
    }

    public Task InsertVerticesAsync(IReadOnlyList<GraphVertex> vertices, CancellationToken token)
        => InsertAsync(InsertVertices, vertices.Count, writer =>
        {
            foreach (var vertex in vertices)
            {
                writer.WriteStartObject();
                writer.WriteString(IdParameter, vertex.Id);
                writer.WriteNumber(NumberParameter, vertex.Number);
                writer.WriteEndObject();
            }
        }, token);

    public Task InsertEdgesAsync(IReadOnlyList<GraphEdge> edges, CancellationToken token)
        => InsertAsync(InsertEdges, edges.Count, writer =>
        {
            foreach (var edge in edges)
            {
                writer.WriteStartObject();
                writer.WriteString(IdParameter, edge.Id);
                writer.WriteString(FromParameter, edge.From);
                writer.WriteString(ToParameter, edge.To);
                writer.WriteEndObject();
            }
        }, token);

    public async Task<IReadOnlyList<string>> NeighborsAsync(string vertex, CancellationToken token)
    {
        using var response = await QueryAsync(Neighbors, writer => writer.WriteString(IdParameter, vertex), token);
        return [.. Values(response).EnumerateArray().Select(row => row[0].GetString() ?? throw new InvalidDataException(MissingId))];
    }

    public async Task<int> CountReachableAsync(string vertex, int depth, CancellationToken token)
    {
        var statement = ReachableFrom + depth.ToString(CultureInfo.InvariantCulture) + ReachableEnd;
        using var response = await QueryAsync(statement, writer => writer.WriteString(IdParameter, vertex), token);
        return Values(response)[0][0].GetInt32();
    }

    public ValueTask DisposeAsync()
    {
        http.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>Runs one <c>UNWIND $rows</c> statement and checks that it created one element per row.</summary>
    private async Task InsertAsync(string statement, int count, Action<Utf8JsonWriter> rows, CancellationToken token)
    {
        using var response = await QueryAsync(statement, writer =>
        {
            writer.WriteStartArray(RowsParameter);
            rows(writer);
            writer.WriteEndArray();
        }, token);
        var created = Values(response)[0][0].GetInt32();
        if (created != count)
        {
            throw new InvalidDataException(WrongCount + created.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Runs one statement as an auto-commit transaction; throws with Neo4j's own error when it reports one.</summary>
    private async Task<JsonDocument> QueryAsync(string statement, Action<Utf8JsonWriter> parameters, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, QueryPath)
        {
            Content = new StringContent(Body(statement, parameters), Encoding.UTF8, JsonMediaType)
        };
        request.Headers.Accept.ParseAdd(JsonMediaType);
        using var response = await http.SendAsync(request, token);
        var text = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(Failed + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + Separator + text,
                null, response.StatusCode);
        }
        var document = JsonDocument.Parse(text);
        if (document.RootElement.TryGetProperty(ErrorsProperty, out var errors) && errors.GetArrayLength() > 0)
        {
            document.Dispose();
            throw new InvalidOperationException(Failed + text);
        }
        return document;
    }

    private static string Body(string statement, Action<Utf8JsonWriter> parameters)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteString(StatementProperty, statement);
        writer.WriteStartObject(ParametersProperty);
        parameters(writer);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static JsonElement Values(JsonDocument response)
        => response.RootElement.GetProperty(DataProperty).GetProperty(ValuesProperty);
}
