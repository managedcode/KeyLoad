using System.Numerics;
using System.Text;
using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.Query;

public sealed class SearchEngine(DatabaseEngine database)
{
    public RankedDocument[] Search(string principalId, SearchRequest request)
    {
        if (request.Limit is < 1 or > 1_000 || request.FusionConstant < 1 || !double.IsFinite(request.TextWeight)
            || !double.IsFinite(request.VectorWeight) || request.TextWeight < 0 || request.VectorWeight < 0
            || request.Text is null && request.Vector is null)
            throw Errors.Fail(ErrorCode.Validation, "The search budgets or branch weights are invalid.");
        if (request.Text is { } text && (request.TextField is null || Encoding.UTF8.GetByteCount(text) > 4_096))
            throw Errors.Fail(ErrorCode.Validation, "A bounded text query and field are required.");
        if (request.Vector is { } vector && (request.VectorField is null || request.Space is null
            || request.Space.Dimension is < 1 or > 4_096 || vector.Length != request.Space.Dimension
            || vector.Any(v => !float.IsFinite(v)) || !Enum.IsDefined(request.Space.Metric)))
            throw Errors.Fail(ErrorCode.Validation, "A finite vector and a matching typed vector space are required.");
        // Authorize both modalities and read them under the same apply gate and policy epoch.
        return database.WithQueryView(principalId, request.Partition, request.Collection, (view, principal, resource) =>
        {
            if (request.Text is not null) database.Authorization.RequireFieldUse(principal, resource, request.TextField!);
            if (request.Vector is not null)
            {
                database.Authorization.Require(principal, request.Partition, request.Collection, Capability.VectorSearch);
                database.Authorization.RequireFieldUse(principal, resource, request.VectorField!);
            }
            var branches = new List<(DocumentResult Document, double Score, double Weight)[]>();
            if (request.Text is not null)
                branches.Add(TextBranch(principal, resource, database.ReadVisibleDocuments(view, principal, request.Partition, request.Collection), request));
            if (request.Vector is not null)
                branches.Add(database.ReadVisibleVectors(view, principal, request.Partition, request.Collection, request.VectorField!)
                    .Where(c => c.Vector.Space == request.Space).Select(candidate =>
                    (Document: database.Project(principal, resource, candidate.Document),
                        Score: Similarity(request.Vector, candidate.Vector.Values, request.Space!.Metric), Weight: request.VectorWeight))
                    .OrderByDescending(d => d.Score).ThenBy(d => d.Document.Reference.Id, StringComparer.Ordinal).ToArray());
            var fused = new Dictionary<EntityRef, (DocumentResult Document, double Score)>();
            foreach (var branch in branches)
                for (var rank = 0; rank < branch.Length; rank++)
                {
                    var item = branch[rank];
                    var previous = fused.GetValueOrDefault(item.Document.Reference);
                    fused[item.Document.Reference] = (item.Document, previous.Score + item.Weight / (request.FusionConstant + rank + 1));
                }
            var result = fused.Values.OrderByDescending(d => d.Score).ThenBy(d => d.Document.Reference.Id, StringComparer.Ordinal)
                .Take(request.Limit).Select(d => new RankedDocument(d.Document, d.Score)).ToArray();
            if (result.Sum(d => Encoding.UTF8.GetByteCount(d.Document.Json)) > database.Limits.MaxBatchBytes)
                throw Errors.Fail(ErrorCode.BudgetExceeded, "The search result byte budget is exceeded.");
            return result;
        });
    }
    private (DocumentResult Document, double Score, double Weight)[] TextBranch(PrincipalRecord principal,
        ResourceDefinition resource, DocumentRecord[] documents, SearchRequest request)
    {
        var terms = Terms(request.Text!).Distinct(StringComparer.Ordinal).ToArray();
        var corpus = documents.Select(document =>
        {
            using var json = JsonDocument.Parse(document.Json);
            var value = JsonData.Scalar(json.RootElement, request.TextField!);
            return (Document: document, Terms: value is string content ? Terms(content) : []);
        }).ToArray();
        var average = corpus.Length == 0 ? 1 : Math.Max(1, corpus.Average(d => d.Terms.Length));
        var frequency = terms.ToDictionary(term => term,
            term => corpus.Count(d => d.Terms.Contains(term, StringComparer.Ordinal)), StringComparer.Ordinal);
        return corpus.Select(item =>
        {
            double score = 0;
            foreach (var term in terms)
            {
                var count = item.Terms.Count(t => t == term);
                if (count == 0) continue;
                var idf = Math.Log(1 + (corpus.Length - frequency[term] + 0.5) / (frequency[term] + 0.5));
                score += idf * count * 2.2 / (count + 1.2 * (0.25 + 0.75 * item.Terms.Length / average));
            }
            return (Document: database.Project(principal, resource, item.Document), Score: score, Weight: request.TextWeight);
        }).Where(d => d.Score > 0).OrderByDescending(d => d.Score)
            .ThenBy(d => d.Document.Reference.Id, StringComparer.Ordinal).ToArray();
    }
    private static string[] Terms(string text)
    {
        var words = new List<string>(); var word = new StringBuilder();
        foreach (var rune in text.Normalize(NormalizationForm.FormKC).EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune)) word.Append(Rune.ToLowerInvariant(rune));
            else if (word.Length != 0) { words.Add(word.ToString()); word.Clear(); }
            if (words.Count > 65_536 || word.Length > 4_096) throw Errors.Fail(ErrorCode.BudgetExceeded, "The text token budget is exceeded.");
        }
        if (word.Length != 0) words.Add(word.ToString());
        return words.ToArray();
    }
    public static double Similarity(float[] left, float[] right, DistanceMetric metric)
    {
        if (left.Length != right.Length || left.Length is < 1 or > 4_096
            || left.Any(v => !float.IsFinite(v)) || right.Any(v => !float.IsFinite(v)))
            throw Errors.Fail(ErrorCode.Validation, "Vector dimensions or values are invalid.");
        double dot = 0, leftNorm = 0, rightNorm = 0, distance = 0; var i = 0;
        for (; i + Vector<float>.Count <= left.Length; i += Vector<float>.Count)
        {
            Vector.Widen(new Vector<float>(left, i), out Vector<double> l1, out Vector<double> l2);
            Vector.Widen(new Vector<float>(right, i), out Vector<double> r1, out Vector<double> r2);
            dot += Vector.Dot(l1, r1) + Vector.Dot(l2, r2);
            leftNorm += Vector.Dot(l1, l1) + Vector.Dot(l2, l2);
            rightNorm += Vector.Dot(r1, r1) + Vector.Dot(r2, r2);
            var d1 = l1 - r1; var d2 = l2 - r2;
            distance += Vector.Dot(d1, d1) + Vector.Dot(d2, d2);
        }
        for (; i < left.Length; i++)
        {
            dot += (double)left[i] * right[i]; leftNorm += (double)left[i] * left[i];
            rightNorm += (double)right[i] * right[i]; distance += Math.Pow((double)left[i] - right[i], 2);
        }
        return metric switch
        {
            DistanceMetric.DotProduct => dot, DistanceMetric.Euclidean => -Math.Sqrt(distance),
            DistanceMetric.Cosine => leftNorm == 0 || rightNorm == 0 ? 0 : dot / Math.Sqrt(leftNorm * rightNorm),
            _ => throw Errors.Fail(ErrorCode.Validation, "The vector metric is invalid.")
        };
    }
}
