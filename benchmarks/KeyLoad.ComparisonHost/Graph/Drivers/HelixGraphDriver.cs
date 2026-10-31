using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.ComparisonHost.Deployment;

namespace KeyLoad.ComparisonHost.Graph;

/// <summary>
/// HelixDB graph through its query operation trees (<c>POST /v2/query</c>): <c>Vertex</c> nodes with a unique equality
/// index on <c>id</c>, <c>add_n</c>/<c>add_e</c> write batches acknowledged with <c>X-Helix-Await-Durable</c>, an
/// <c>out</c> step for neighbours and, for the reachable count, <c>count</c> over <c>dedup</c> of the union of the
/// 1..depth <c>out</c> paths without the start.
/// </summary>
/// <remarks>
/// The operation tree has no step that collects every depth up to a bound (its <c>repeat</c> runs a fixed number of hops),
/// so the 1..depth reach is the union of one fixed-length path per depth; HelixDB itself removes duplicates and the start
/// and counts the result.
/// </remarks>
internal sealed class HelixGraphDriver(BenchmarkConnection connection) : IGraphDriver
{
    private const string Route = "v2/query";
    private const string JsonMediaType = "application/json";
    private const string DurableHeader = "X-Helix-Await-Durable";
    private const string Durable = "true";
    private const string Kind = "kind";
    private const string Accepted = "accepted";
    private const string Existing = "existing_operation";
    private const string OperationId = "operation_id";
    private const string Status = "status";
    private const string IndexSucceeded = "succeeded";
    private const string IndexBlocked = "blocked";
    private const string IndexAborted = "aborted";
    private const string Failed = "HelixDB ";
    private const string Separator = ": ";
    private const string IndexRejected = "HelixDB did not accept the id index: ";
    private const string IndexFailed = "HelixDB did not build the id index: ";
    private const string NotCreated = "HelixDB did not create ";
    private const string MissingId = "HelixDB returned a neighbour without an id.";
    private const int Batch = 256;
    private const int Created = 1;
    private static readonly TimeSpan IndexPoll = TimeSpan.FromMilliseconds(200);
    private static readonly Uri QueryPath = new(Route, UriKind.Relative);
    private readonly HttpClient http = connection.CreateHttpClient(0);

    public int BatchSize => Batch;

    /// <summary>Creates the unique id index and waits until HelixDB reports its build operation succeeded.</summary>
    public async Task PrepareAsync(CancellationToken token)
    {
        var operation = await CreateIndexAsync(token);
        while (true)
        {
            using var response = await QueryAsync(HelixAst.IndexOperation(operation), false, token);
            var state = HelixAst.First(response);
            var status = state.GetProperty(Status).GetString();
            if (status == IndexSucceeded)
            {
                return;
            }
            if (status is IndexBlocked or IndexAborted)
            {
                throw new InvalidOperationException(IndexFailed + state.GetRawText());
            }
            await Task.Delay(IndexPoll, TimeProvider.System, token);
        }
    }

    public Task InsertVerticesAsync(IReadOnlyList<GraphVertex> vertices, CancellationToken token)
    {
        var names = vertices.Select(vertex => HelixAst.Variable(vertex.Id)).ToArray();
        return WriteAsync(vertices.Select((vertex, index) => HelixAst.Entry(names[index], HelixAst.Count(HelixAst.AddVertex(vertex)))),
            names, token);
    }

    public Task InsertEdgesAsync(IReadOnlyList<GraphEdge> edges, CancellationToken token)
    {
        var names = edges.Select(edge => HelixAst.Variable(edge.Id)).ToArray();
        return WriteAsync(edges.SelectMany((edge, index) => HelixAst.AddLink(edge, names[index])), names, token);
    }

    public async Task<IReadOnlyList<string>> NeighborsAsync(string vertex, CancellationToken token)
    {
        using var response = await QueryAsync(HelixAst.Neighbors(vertex), false, token);
        return [.. HelixAst.Rows(response).EnumerateArray()
            .Select(row => row.GetProperty(HelixAst.IdProperty).GetString() ?? throw new InvalidDataException(MissingId))];
    }

    public async Task<int> CountReachableAsync(string vertex, int depth, CancellationToken token)
    {
        using var response = await QueryAsync(HelixAst.Reachable(vertex, depth), false, token);
        return checked((int)HelixAst.Number(HelixAst.Rows(response)));
    }

    public ValueTask DisposeAsync()
    {
        http.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<string> CreateIndexAsync(CancellationToken token)
    {
        using var response = await QueryAsync(HelixAst.UniqueIdIndex(), true, token);
        var receipt = HelixAst.First(response);
        if (receipt.GetProperty(Kind).GetString() is not (Accepted or Existing))
        {
            throw new InvalidOperationException(IndexRejected + receipt.GetRawText());
        }
        return receipt.GetProperty(OperationId).GetString() ?? throw new InvalidOperationException(IndexRejected + receipt.GetRawText());
    }

    /// <summary>Commits the entries as one write batch and checks that every named entry created exactly one element.</summary>
    private async Task WriteAsync(IEnumerable<JsonObject> entries, string[] names, CancellationToken token)
    {
        using var response = await SendAsync(HelixAst.Batch(true, new JsonArray([.. entries]), HelixAst.Names(names)), true, token);
        foreach (var name in names)
        {
            if (!response.RootElement.TryGetProperty(name, out var created) || HelixAst.Number(created) != Created)
            {
                throw new InvalidDataException(NotCreated + name);
            }
        }
    }

    /// <summary>Runs one operation tree as a batch of one entry returned as <c>rows</c>.</summary>
    private Task<JsonDocument> QueryAsync(JsonObject root, bool write, CancellationToken token)
        => SendAsync(HelixAst.Batch(write, new JsonArray(HelixAst.Entry(HelixAst.RowsName, root)), HelixAst.Names([HelixAst.RowsName])),
            write, token);

    private async Task<JsonDocument> SendAsync(JsonObject batch, bool write, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, QueryPath)
        {
            Content = new StringContent(batch.ToJsonString(), Encoding.UTF8, JsonMediaType)
        };
        if (write)
        {
            request.Headers.Add(DurableHeader, Durable);
        }
        using var response = await http.SendAsync(request, token);
        var text = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(Failed + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + Separator + text,
                null, response.StatusCode);
        }
        var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.TryGetProperty(HelixAst.ErrorProperty, out _))
        {
            document.Dispose();
            throw new InvalidOperationException(Failed + text);
        }
        return document;
    }
}

/// <summary>Builds HelixDB query operation trees; every step holds its predecessor under <c>input</c>.</summary>
static file class HelixAst
{
    internal const string IdProperty = "id";
    internal const string ErrorProperty = "error";
    internal const string RowsName = "rows";
    private const string VertexLabel = "Vertex";
    private const string LinkLabel = "Link";
    private const string NumberProperty = "number";
    private const string RequestType = "request_type";
    private const string SearchConsistency = "search_consistency";
    private const string Strong = "strong";
    private const string Query = "query";
    private const string Read = "read";
    private const string Write = "write";
    private const string Entries = "entries";
    private const string Returns = "returns";
    private const string Name = "name";
    private const string Root = "root";
    private const string Input = "input";
    private const string Label = "label";
    private const string Properties = "properties";
    private const string Predicate = "predicate";
    private const string Predicates = "predicates";
    private const string Left = "left";
    private const string Right = "right";
    private const string Property = "property";
    private const string Value = "value";
    private const string Constant = "constant";
    private const string Text = "string";
    private const string Integer = "i64";
    private const string MetaLabel = "$label";
    private const string CountStep = "count";
    private const string AddNode = "add_n";
    private const string AddEdge = "add_e";
    private const string Nodes = "nodes";
    private const string NodesWhere = "nodes_where";
    private const string Reference = "reference";
    private const string VariableKey = "var";
    private const string To = "to";
    private const string Out = "out";
    private const string Union = "union";
    private const string Traversals = "traversals";
    private const string Context = "context";
    private const string Dedup = "dedup";
    private const string Where = "where";
    private const string ValueMap = "value_map";
    private const string Equal = "eq";
    private const string NotEqual = "neq";
    private const string And = "and";
    private const string CreateIndex = "create_index";
    private const string IfNotExists = "if_not_exists";
    private const string Spec = "spec";
    private const string NodeEquality = "node_equality";
    private const string Unique = "unique";
    private const string GetIndexOperation = "get_index_operation";
    private const string OperationId = "operation_id";
    private const string FromPrefix = "from_";
    private const string ToPrefix = "to_";
    private const char IdSeparator = '-';
    private const char NameSeparator = '_';

    /// <summary>An entry name: the id with its separators replaced, since names are identifiers.</summary>
    internal static string Variable(string id) => id.Replace(IdSeparator, NameSeparator);

    internal static JsonObject Batch(bool write, JsonArray entries, JsonArray returns)
    {
        var kind = write ? Write : Read;
        return new()
        {
            [RequestType] = kind,
            [SearchConsistency] = Strong,
            [Query] = new JsonObject { [kind] = new JsonObject { [Entries] = entries, [Returns] = returns } }
        };
    }

    internal static JsonObject Entry(string name, JsonObject root)
        => new() { [Query] = new JsonObject { [Name] = name, [Root] = root } };

    internal static JsonArray Names(IEnumerable<string> names) => new([.. names.Select(name => (JsonNode?)name)]);

    internal static JsonElement Rows(JsonDocument response) => response.RootElement.GetProperty(RowsName);

    internal static JsonElement First(JsonDocument response)
    {
        var rows = Rows(response);
        return rows.ValueKind == JsonValueKind.Array && rows.GetArrayLength() == 1 ? rows[0] : rows;
    }

    /// <summary>A count result, returned as <c>[n]</c> or <c>n</c>.</summary>
    internal static long Number(JsonElement value)
        => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 1 ? value[0].GetInt64() : value.GetInt64();

    internal static JsonObject UniqueIdIndex() => Step(CreateIndex, new()
    {
        [IfNotExists] = false,
        [Spec] = Step(NodeEquality, new() { [Label] = VertexLabel, [Property] = IdProperty, [Unique] = true })
    });

    internal static JsonObject IndexOperation(string operation) => Step(GetIndexOperation, new() { [OperationId] = operation });

    internal static JsonObject Count(JsonNode input) => Step(CountStep, new() { [Input] = input });

    internal static JsonObject AddVertex(GraphVertex vertex) => Step(AddNode, new()
    {
        [Label] = VertexLabel,
        [Properties] = new JsonArray(Field(IdProperty, Text, vertex.Id), Field(NumberProperty, Integer, vertex.Number))
    });

    /// <summary>Looks up both endpoints, then adds the edge between them; the edge entry counts what it created.</summary>
    internal static JsonObject[] AddLink(GraphEdge edge, string name)
    {
        var from = FromPrefix + name;
        var to = ToPrefix + name;
        var link = Step(AddEdge, new()
        {
            [Input] = Step(Nodes, new() { [Reference] = new JsonObject { [VariableKey] = from } }),
            [Label] = LinkLabel,
            [To] = new JsonObject { [VariableKey] = to },
            [Properties] = new JsonArray(Field(IdProperty, Text, edge.Id))
        });
        return [Entry(from, Lookup(edge.From)), Entry(to, Lookup(edge.To)), Entry(name, Count(link))];
    }

    internal static JsonObject Neighbors(string id)
        => Step(ValueMap, new() { [Input] = OutStep(Lookup(id)), [Properties] = new JsonArray(IdProperty) });

    internal static JsonObject Reachable(string id, int depth)
    {
        var paths = new JsonArray();
        for (var hops = 1; hops <= depth; hops++)
        {
            JsonNode path = JsonValue.Create(Context);
            for (var hop = 0; hop < hops; hop++)
            {
                path = OutStep(path);
            }
            paths.Add(new JsonObject { [Root] = path });
        }
        var union = Step(Union, new() { [Input] = Lookup(id), [Traversals] = paths });
        var distinct = Step(Dedup, new() { [Input] = union });
        return Count(Step(Where, new() { [Input] = distinct, [Predicate] = Compare(NotEqual, IdProperty, id) }));
    }

    private static JsonObject Lookup(string id) => Step(NodesWhere, new()
    {
        [Predicate] = Step(And, new()
        {
            [Predicates] = new JsonArray(Compare(Equal, MetaLabel, VertexLabel), Compare(Equal, IdProperty, id))
        })
    });

    private static JsonObject OutStep(JsonNode input) => Step(Out, new() { [Input] = input, [Label] = LinkLabel });

    private static JsonObject Compare(string operation, string property, string value) => Step(operation, new()
    {
        [Left] = new JsonObject { [Property] = property },
        [Right] = new JsonObject { [Constant] = new JsonObject { [Text] = value } }
    });

    private static JsonArray Field(string name, string kind, JsonNode? value)
        => new(name, new JsonObject { [Value] = new JsonObject { [kind] = value } });

    private static JsonObject Step(string operation, JsonObject fields) => new() { [operation] = fields };
}
