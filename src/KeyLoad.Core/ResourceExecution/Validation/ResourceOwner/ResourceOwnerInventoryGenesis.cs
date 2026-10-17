using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Proves only the original authentication-only native store cut; never infers an inventory from old data.</summary>
internal static class ResourceOwnerInventoryGenesis
{
    private const long AuthenticationBootstrapPosition = 1;
    private const long NoExaminedBytes = 0;
    private const int AuthenticationKeyComponents = 2;
    private const int AuthenticationIdentifierIndex = 1;

    internal static bool CanInitialize(IKeyValueView view, StoreIdentity identity, long position,
        PhysicalShardRecord owner, DatabaseLimits limits)
    {
        if (position != AuthenticationBootstrapPosition || identity.Incarnation != owner.Incarnation)
        {
            return false;
        }
        var withinBudget = true;
        var examinedBytes = NoExaminedBytes;
        var census = view.VisitRange([], limits.MaxScanRecords,
            (key, _) => withinBudget && IsAuthenticationKey(key), observer: bytes =>
            {
                if (bytes < NoExaminedBytes || bytes > limits.MaxQueryReadBytes - examinedBytes)
                {
                    withinBudget = false;
                    return;
                }
                examinedBytes += bytes;
            });
        return withinBudget && !census.HasMore && !census.StoppedByVisitor;
    }

    private static bool IsAuthenticationKey(ReadOnlySpan<byte> key)
    {
        try
        {
            var parts = KeyCodec.Decode(key);
            return parts.Length == AuthenticationKeyComponents
                && parts[AuthenticationIdentifierIndex] is string identifier
                && !string.IsNullOrWhiteSpace(identifier)
                && (key.SequenceEqual(KeySpace.Principal(identifier))
                    || key.SequenceEqual(KeySpace.ApiKey(identifier)));
        }
        catch (KeyLoadException)
        {
            return false;
        }
    }
}
