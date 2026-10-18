namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Checks the exact local image tag and its matching relative receipt path.</summary>
internal static class LocalClusterImageReferenceValidation
{
    internal static bool IsCanonicalReferenceAndReceipt(string reference, string receipt)
    {
        var separator = reference.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0 || separator == reference.Length - 1)
        {
            return false;
        }
        var tag = reference[(separator + 1)..];
        if (tag.Length != 38 || !tag.StartsWith("local-", StringComparison.Ordinal)
            || tag[6..].Length != 32 || tag[6..].Any(character => !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
        {
            return false;
        }
        return receipt == $"TestResults/cluster/local-images/image-{tag[6..]}.json";
    }
}
