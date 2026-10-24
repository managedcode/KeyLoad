using System.Globalization;

namespace KeyLoad.Comparisons.Targets;

internal static class Neo4jQueryRequests
{
    private const string InitializeAsyncCREATECONSTRAINTText = "CREATE CONSTRAINT ";
    private const string InitializeAsyncIdFORNText = "_id FOR (n:";
    private const string InitializeAsyncREQUIRENIdISUNIQUEText = ") REQUIRE n.id IS UNIQUE";
    private const string InitializeAsyncUNWINDDocumentsASDCREATENText = "UNWIND $documents AS d CREATE (n:";
    private const string InitializeAsyncIdDIdJsonDJsonText = " {id:d.id,json:d.json})";
    private const string InitializeAsyncUNWINDEdgesASEMATCHAText = "UNWIND $edges AS e MATCH (a:";
    private const string InitializeAsyncIdEFromBText = " {id:e.from}),(b:";
    private const string InitializeAsyncIdEToCREATEALINKSBText = " {id:e.to}) CREATE (a)-[:LINKS]->(b)";
    private const string DisposeAsyncMATCHNText = "MATCH (n:";
    private const string DisposeAsyncWITHNLIMITText = ") WITH n LIMIT ";
    private const string DisposeAsyncDETACHDELETENRETURNCountText = " DETACH DELETE n RETURN count(*)";
    private const string DisposeAsyncDROPCONSTRAINTText = "DROP CONSTRAINT ";
    private const string DisposeAsyncIdIFEXISTSText = "_id IF EXISTS";

    internal static string Constraint(string label)
        => $"{InitializeAsyncCREATECONSTRAINTText}{label}{InitializeAsyncIdFORNText}{label}{InitializeAsyncREQUIRENIdISUNIQUEText}";

    internal static string Documents(string label)
        => $"{InitializeAsyncUNWINDDocumentsASDCREATENText}{label}{InitializeAsyncIdDIdJsonDJsonText}";

    internal static string Edges(string label)
        => $"{InitializeAsyncUNWINDEdgesASEMATCHAText}{label}{InitializeAsyncIdEFromBText}{label}{InitializeAsyncIdEToCREATEALINKSBText}";

    internal static string DeleteNodeBatch(string label, int batch)
        => $"{DisposeAsyncMATCHNText}{label}{DisposeAsyncWITHNLIMITText}{batch.ToString(CultureInfo.InvariantCulture)}{DisposeAsyncDETACHDELETENRETURNCountText}";

    internal static string DropConstraint(string label)
        => $"{DisposeAsyncDROPCONSTRAINTText}{label}{DisposeAsyncIdIFEXISTSText}";

    private const string ReadCorpusAsyncMATCHNText = "MATCH (n:";
    // A plain range predicate lets the uniqueness constraint's range index seek past the last key and supply the order;
    // an OR with a null test forces a label scan and sort of the whole namespace for every page.
    private const string ReadCorpusAsyncWHERENIdAfterRETURNNIdNJsonORDERBYNIdLIMITText = ") WHERE n.id > $after RETURN n.id,n.json ORDER BY n.id LIMIT ";
    private const string ReadAsyncIdIdRETURNNJsonText = " {id:$id}) RETURN n.json";
    private const string ExecuteAsyncMATCHAText = "MATCH (a:";
    private const string ExecuteAsyncIdIdLINKSText = " {id:$id})-[:LINKS*1..";
    private const string ExecuteAsyncBText = "]->(b:";
    private const string ExecuteAsyncWHEREBIdIdRETURNDISTINCTBIdORDERBYBIdText = ") WHERE b.id<>$id RETURN DISTINCT b.id ORDER BY b.id";

    internal static string Corpus(string label, int capacity)
        => $"{ReadCorpusAsyncMATCHNText}{label}{ReadCorpusAsyncWHERENIdAfterRETURNNIdNJsonORDERBYNIdLIMITText}{capacity}";

    internal static string Read(string label)
        => $"{ReadCorpusAsyncMATCHNText}{label}{ReadAsyncIdIdRETURNNJsonText}";

    internal static string Traverse(string label, int hops)
        => $"{ExecuteAsyncMATCHAText}{label}{ExecuteAsyncIdIdLINKSText}{hops}{ExecuteAsyncBText}{label}{ExecuteAsyncWHEREBIdIdRETURNDISTINCTBIdORDERBYBIdText}";

    // Every corpus identifier is non-empty, so the empty string orders before the first page.
    internal static object After(string? after)
        => new
        {
            after = after ?? string.Empty
        };

    internal static object Identifier(string id)
        => new
        {
            id
        };

    internal static object DocumentsPayload(IEnumerable<BenchmarkDocument> batch)
        => new
        {
            documents = batch.Select(document => new
            {
                id = document.Id,
                json = document.Json
            }).ToArray()
        };

    internal static object EdgesPayload(IEnumerable<BenchmarkEdge> batch)
        => new
        {
            edges = batch.Select(edge => new
            {
                from = edge.From,
                to = edge.To
            }).ToArray()
        };
}
