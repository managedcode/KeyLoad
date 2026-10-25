using System.Globalization;

namespace KeyLoad.ClusterTests.Search;

/// <summary>Describes only closed field differences for the fixed three-document execution Orleans corpus.</summary>
internal static class TextClusterResultDiagnostic
{
    private const int FirstRow = 0;
    private const string Prefix = "Static execution text result difference";

    internal static string Describe(string path, RankedDocument[] actual, RankedDocument[] expected)
    {
        var rows = new List<string>();
        var count = Math.Min(TextClusterScenario.CorpusCount, Math.Min(actual.Length, expected.Length));
        for (var index = FirstRow; index < count; index++)
        {
            var left = actual[index];
            var right = expected[index];
            var fields = new List<string>();
            if (left.Document.Reference != right.Document.Reference)
            {
                fields.Add("Reference");
            }
            if (left.Document.Revision != right.Document.Revision)
            {
                fields.Add("Revision");
            }
            if (left.Document.Json != right.Document.Json)
            {
                fields.Add("ProjectedJson");
            }
            if (left.Document.Redacted != right.Document.Redacted)
            {
                fields.Add("Redacted");
            }
            if (!left.Document.RedactedFields.SequenceEqual(right.Document.RedactedFields))
            {
                fields.Add("RedactedFields");
            }
            if (BitConverter.DoubleToInt64Bits(left.Score) != BitConverter.DoubleToInt64Bits(right.Score))
            {
                fields.Add("ScoreBits");
            }
            if (!JsonDefaults.Serialize(left.Explanation).AsSpan().SequenceEqual(JsonDefaults.Serialize(right.Explanation)))
            {
                fields.Add("Explanation");
            }
            rows.Add(index.ToString(CultureInfo.InvariantCulture) + ":" + string.Join(',', fields));
        }
        return Prefix + " path=" + path + " count=" + actual.Length.ToString(CultureInfo.InvariantCulture)
            + "/" + expected.Length.ToString(CultureInfo.InvariantCulture) + " rows=" + string.Join(';', rows);
    }
}
