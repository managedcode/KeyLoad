using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class DocumentSessionAuthorityFault
{
    private const int Missing = 0;
    private const int Negative = 1;
    private const int Truncated = 2;
    private const long InvalidPosition = -1;
    private const int RemovedByte = 1;
    private const string Invalid = "The original session authority fault selection is invalid.";

    internal static byte[] Original(RemoteDocumentNativeFixture fixture)
        => fixture.Destination.Store.Read(view => view.ReadOwnedValue(KeySpace.AppliedBytes))
            ?? throw new InvalidOperationException(Invalid);

    internal static void Damage(RemoteDocumentNativeFixture fixture, byte[] original, int kind)
    {
        var changed = kind switch
        {
            Missing => null,
            Negative => NativeSerialization.Serialize(InvalidPosition),
            Truncated => original[..^RemovedByte],
            _ => throw new InvalidOperationException(Invalid)
        };
        fixture.Destination.Store.Commit((tx, _) =>
        {
            if (changed is null)
            { tx.Delete(KeySpace.AppliedBytes); }
            else
            { tx.Put(KeySpace.AppliedBytes, changed); }
            return true;
        });
    }

    internal static void Repair(RemoteDocumentNativeFixture fixture, byte[] original)
        => fixture.Destination.Store.Commit((tx, _) => { tx.Put(KeySpace.AppliedBytes, original); return true; });

    internal static string Detail(int kind) => kind == Truncated
        ? "The internal binary payload is invalid." : "The canonical applied position is invalid.";
}
