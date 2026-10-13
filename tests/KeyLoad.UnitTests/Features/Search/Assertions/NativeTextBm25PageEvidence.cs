namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextBm25PageEvidence
{
    internal static string Describe(RankedDocument[] expected, RankedDocument[] actual,
        byte[] expectedBytes, byte[] actualBytes)
    {
        var references = true;
        var revisions = true;
        var json = true;
        var redacted = true;
        var fields = true;
        var scores = true;
        var explanations = true;
        for (var index = 0; index < Math.Min(expected.Length, actual.Length); index++)
        {
            var left = expected[index];
            var right = actual[index];
            references &= left.Document.Reference == right.Document.Reference;
            revisions &= left.Document.Revision == right.Document.Revision;
            json &= string.Equals(left.Document.Json, right.Document.Json, StringComparison.Ordinal);
            redacted &= left.Document.Redacted == right.Document.Redacted;
            fields &= left.Document.RedactedFields.AsSpan().SequenceEqual(right.Document.RedactedFields.AsSpan());
            scores &= BitConverter.DoubleToInt64Bits(left.Score) == BitConverter.DoubleToInt64Bits(right.Score);
            explanations &= Equals(left.Explanation, right.Explanation);
        }
        var firstByte = 0;
        while (firstByte < Math.Min(expectedBytes.Length, actualBytes.Length)
            && expectedBytes[firstByte] == actualBytes[firstByte])
        { firstByte++; }
        return $"BM25 page count expected={expected.Length} actual={actual.Length}; "
            + $"bytes expected={expectedBytes.Length} actual={actualBytes.Length} firstDifference={firstByte}; "
            + $"reference={references} revision={revisions} json={json} redacted={redacted} "
            + $"redactedFields={fields} scoreBits={scores} explanation={explanations}.";
    }
}
