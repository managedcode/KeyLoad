namespace KeyLoad.FunctionalTests.Search;

internal static class TextBm25PageEvidence
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
        var aliases = true;
        var fieldsDefault = true;
        var runtimeTypes = expected.GetType() == actual.GetType();
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
            aliases &= SameAliases(left, right);
            fieldsDefault &= left.Document.RedactedFields.IsDefault == right.Document.RedactedFields.IsDefault;
            runtimeTypes &= left.GetType() == right.GetType() && left.Document.GetType() == right.Document.GetType()
                && left.Document.Reference.GetType() == right.Document.Reference.GetType()
                && left.Document.Reference.Partition.GetType() == right.Document.Reference.Partition.GetType();
            if (index > 0)
            {
                aliases &= ReferenceEquals(expected[index - 1].Document.Reference.Partition, left.Document.Reference.Partition)
                == ReferenceEquals(actual[index - 1].Document.Reference.Partition, right.Document.Reference.Partition);
            }
        }
        var firstByte = 0;
        while (firstByte < Math.Min(expectedBytes.Length, actualBytes.Length)
            && expectedBytes[firstByte] == actualBytes[firstByte])
        {
            firstByte++;
        }
        return $"BM25 page count expected={expected.Length} actual={actual.Length}; "
            + $"bytes expected={expectedBytes.Length} actual={actualBytes.Length} firstDifference={firstByte}; "
            + $"reference={references} revision={revisions} json={json} redacted={redacted} "
            + $"redactedFields={fields} scoreBits={scores} explanation={explanations}; "
            + $"selectedAliases={aliases} redactedFieldsDefault={fieldsDefault} runtimeTypes={runtimeTypes}.";
    }

    private static bool SameAliases(RankedDocument expected, RankedDocument actual)
    {
        static object?[] Slots(RankedDocument row)
        {
            var document = row.Document;
            var reference = document.Reference;
            var partition = reference.Partition;
            return [row, document, reference, partition, partition.TenantId, partition.DatabaseId,
                partition.TransactionDomainId, partition.PartitionKey, reference.Collection, reference.Id,
                document.Json, row.Explanation];
        }
        var left = Slots(expected);
        var right = Slots(actual);
        for (var index = 0; index < left.Length; index++)
        {
            for (var previous = 0; previous < index; previous++)
            {
                if (ReferenceEquals(left[index], left[previous]) != ReferenceEquals(right[index], right[previous]))
                {
                    return false;
                }
            }
        }
        return true;
    }
}
