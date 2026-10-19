using System.Security.Cryptography;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Search;

internal static class TextCanonicalStateCapture
{
    internal static TextCanonicalState Capture(ZoneTreeStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var accumulator = new TextCanonicalStateAccumulator(hash);
        var scan = store.Read(view => view.VisitRange([], TextCrashProtocol.MaximumCanonicalRecords,
            accumulator.Append,
            observer: accumulator.Observe));
        return accumulator.Complete(scan, hash.GetHashAndReset());
    }
}
