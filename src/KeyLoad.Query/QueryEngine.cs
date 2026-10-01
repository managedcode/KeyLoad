using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query;

public sealed class QueryEngine(DatabaseEngine database)
{
    private readonly SemaphoreSlim admission = new(database.Limits.MaxConcurrentQueries);
    private sealed record CursorClaims(string Purpose, Guid Incarnation, string PrincipalId, long PolicyEpoch,
        long SchemaVersion, string QueryHash, long CutPosition, int Offset, DateTimeOffset ExpiresAt);
    public QueryPage Execute(string principalId, QueryRequest request)
    {
        if (!admission.Wait(0)) throw Errors.Fail(ErrorCode.ResourceExhausted, "The query concurrency budget is exhausted.");
        try
        {
            var query = new SqlParser(request.Sql, database.Limits).Parse();
            var hash = JsonData.Fingerprint(new { request.Partition, request.Sql, request.Parameters, request.AllowFullScan });
            var started = Stopwatch.StartNew();
            return database.WithQueryView(principalId, request.Partition, query.Collection, (view, principal, resource) =>
            {
                // Bind all predicates and ordering before any index or document lookup.
                foreach (var field in PredicateEvaluator.Fields(query.Filter).Concat(query.Order.Select(o => o.Path)))
                    database.Authorization.RequireFieldUse(principal, resource, field);
                CheckParameters(query.Filter, request.Parameters);
                var (documents, accessPath) = Candidates(view, principal, resource, request, query);
                var cursorOffset = 0;
                if (request.Cursor is { } cursor)
                {
                    var claims = database.Verify<CursorClaims>(cursor);
                    if (claims.Purpose != "query-page" || claims.Incarnation != database.Store.Identity.Incarnation
                        || claims.PrincipalId != principal.Id || claims.PolicyEpoch != principal.PolicyEpoch || claims.SchemaVersion != resource.SchemaVersion
                        || claims.QueryHash != hash || claims.CutPosition != database.Store.Position || claims.ExpiresAt < DateTimeOffset.UtcNow
                        || claims.Offset < 0 || claims.Offset > database.Limits.MaxScanRecords)
                        throw Errors.Fail(ErrorCode.CursorExpired, "The query cursor no longer has a valid authorized read cut.");
                    cursorOffset = claims.Offset;
                }
                if (query.Explain) return new QueryPage([new("explain", 0, JsonSerializer.Serialize(new { AccessPath = accessPath,
                    AtomicPartition = request.Partition.AtomicPartitionId, ScanBudget = database.Limits.MaxScanRecords }, JsonDefaults.Options))], null, database.Store.Position, accessPath);
                var eligible = new List<DocumentRecord>();
                foreach (var document in documents)
                {
                    if (started.Elapsed > TimeSpan.FromSeconds(database.Limits.QueryDeadlineSeconds))
                        throw Errors.Fail(ErrorCode.BudgetExceeded, "The query deadline is exceeded.");
                    using var json = JsonDocument.Parse(document.Json);
                    if (query.Filter is null || PredicateEvaluator.Evaluate(query.Filter, document, json.RootElement, request.Parameters) == true)
                        eligible.Add(document);
                }
                eligible.Sort((left, right) => CompareRows(left, right, query.Order));
                var page = eligible.Skip(cursorOffset).Take(query.Limit).Select(document => Project(principal, resource, document, query.Projection)).ToArray();
                if (page.Sum(row => System.Text.Encoding.UTF8.GetByteCount(row.Json)) > database.Limits.MaxBatchBytes)
                    throw Errors.Fail(ErrorCode.BudgetExceeded, "The query result byte budget is exceeded.");
                var next = cursorOffset + page.Length;
                var token = next < eligible.Count ? database.Sign(new CursorClaims("query-page", database.Store.Identity.Incarnation, principal.Id,
                    principal.PolicyEpoch, resource.SchemaVersion, hash, database.Store.Position, next, DateTimeOffset.UtcNow.AddMinutes(5))) : null;
                return new QueryPage(page, token, database.Store.Position, accessPath);
            });
        }
        finally { admission.Release(); }
    }
    private (DocumentRecord[] Records, string Path) Candidates(IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, QueryRequest request, SelectQuery query)
    {
        var equality = Equalities(query.Filter, request.Parameters).GroupBy(p => p.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);
        if (equality.TryGetValue("/@id", out var identifier) && identifier is string id)
        {
            var record = database.ReadVisibleDocument(view, principal, new(request.Partition, query.Collection, id));
            return (record is null ? [] : [record], "point");
        }
        foreach (var index in resource.Indexes.OrderByDescending(i => i.Fields.Length))
        {
            var prefixFields = index.Fields.TakeWhile(equality.ContainsKey).ToArray();
            if (prefixFields.Length == 0) continue;
            var values = prefixFields.Select(f => equality[f]).ToArray();
            if (values.Any(v => v is null || v is MissingValue)) continue;
            var prefix = KeySpace.Partition("index", request.Partition, new object?[] { query.Collection, index.Name }.Concat(values).ToArray());
            var matches = view.Scan(prefix, database.Limits.MaxScanRecords);
            if (matches.HasMore) throw Errors.Fail(ErrorCode.BudgetExceeded, "The index range exceeds its candidate budget.");
            var records = matches.Records.Select(m => database.ReadVisibleDocument(view, principal,
                new(request.Partition, query.Collection, JsonDefaults.Deserialize<string>(m.Value)))).Where(d => d is not null).Cast<DocumentRecord>().ToArray();
            return (records, "index:" + index.Name);
        }
        if (!request.AllowFullScan) throw Errors.Fail(ErrorCode.UnsupportedCapability, "The query requires an index or explicit full-scan permission.");
        var page = view.Scan(KeySpace.Partition("document", request.Partition, query.Collection), database.Limits.MaxScanRecords);
        if (page.HasMore) throw Errors.Fail(ErrorCode.BudgetExceeded, "The full scan exceeds its candidate budget.");
        return (page.Records.Select(m => JsonDefaults.Deserialize<DocumentRecord>(m.Value))
            .Where(d => !d.Deleted && database.Authorization.CanReadRow(principal, d.Access)).ToArray(), "bounded-full-scan");
    }
    private static IEnumerable<KeyValuePair<string, object?>> Equalities(Predicate? predicate, Dictionary<string, JsonElement>? parameters)
    {
        if (predicate is Logical { Operator: "AND" } and)
        { foreach (var pair in Equalities(and.Left, parameters).Concat(Equalities(and.Right, parameters))) yield return pair; }
        else if (predicate is Comparison { Operator: "=", Left: FieldOperand field, Right: ValueOperand value }) yield return new(field.Path, value.Value);
        else if (predicate is Comparison { Operator: "=", Left: FieldOperand parameterField, Right: ParameterOperand parameter }
            && parameters?.TryGetValue(parameter.Name, out var literal) == true) yield return new(parameterField.Path, JsonData.Scalar(literal, ""));
    }
    private static void CheckParameters(Predicate? predicate, Dictionary<string, JsonElement>? parameters)
    {
        void Check(Operand operand)
        {
            if (operand is ParameterOperand parameter && (parameters is null || !parameters.ContainsKey(parameter.Name)))
                throw Errors.Fail(ErrorCode.Validation, "A required query parameter is missing.");
        }
        switch (predicate)
        {
            case Comparison comparison: Check(comparison.Left); Check(comparison.Right); break;
            case Logical logical: CheckParameters(logical.Left, parameters); CheckParameters(logical.Right, parameters); break;
            case Negation not: CheckParameters(not.Inner, parameters); break;
            case NullTest test: Check(test.Value); break;
            case InPredicate list: Check(list.Value); foreach (var operand in list.Values) Check(operand); break;
        }
    }
    private static int CompareRows(DocumentRecord left, DocumentRecord right, Ordering[] order)
    {
        using var leftJson = JsonDocument.Parse(left.Json);
        using var rightJson = JsonDocument.Parse(right.Json);
        foreach (var item in order)
        {
            var l = PredicateEvaluator.Value(new FieldOperand(item.Path), left, leftJson.RootElement, null);
            var r = PredicateEvaluator.Value(new FieldOperand(item.Path), right, rightJson.RootElement, null);
            var result = KeyCodec.Encode(l).AsSpan().SequenceCompareTo(KeyCodec.Encode(r));
            if (result != 0) return item.Descending ? -Math.Sign(result) : Math.Sign(result);
        }
        return KeyCodec.Encode(left.Reference.Id).AsSpan().SequenceCompareTo(KeyCodec.Encode(right.Reference.Id));
    }
    private QueryRow Project(PrincipalRecord principal, ResourceDefinition resource, DocumentRecord document, Selection[] selections)
    {
        var safe = database.Project(principal, resource, document);
        if (selections.Length == 1 && selections[0].Path == "*")
            return new(document.Reference.Id, document.Revision, safe.Json, safe.Redacted, safe.RedactedFields);
        using var json = JsonDocument.Parse(safe.Json);
        var result = new JsonObject();
        foreach (var selection in selections)
        {
            var value = PredicateEvaluator.Value(new FieldOperand(selection.Path), document, json.RootElement, null);
            result[selection.Alias] = value is MissingValue ? null : JsonSerializer.SerializeToNode(value, JsonDefaults.Options);
        }
        return new(document.Reference.Id, document.Revision, result.ToJsonString(), safe.Redacted, safe.RedactedFields);
    }
}
