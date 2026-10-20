namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class DocumentSessionAuthorityFault
{
    private const int Missing = 0;
    private const int Negative = 1;
    private const int Truncated = 2;
    private const long InvalidPosition = -1;
    private const int RemovedByte = 1;
    private const string Invalid = "The original session authority fault selection is invalid.";

    internal static byte[] Original(DocumentPatchAtomicityOwner fixture)
        => fixture.Store.Read(view => view.ReadOwnedValue(KeySpace.AppliedBytes))
            ?? throw new InvalidOperationException(Invalid);

    internal static void Damage(DocumentPatchAtomicityOwner fixture, byte[] original, int kind)
    {
        var changed = kind switch
        {
            Missing => null,
            Negative => OrleansSerialization.Serialize(InvalidPosition),
            Truncated => original[..^RemovedByte],
            _ => throw new InvalidOperationException(Invalid)
        };
        fixture.Store.Commit((tx, _) =>
        {
            if (changed is null)
            {
                tx.Delete(KeySpace.AppliedBytes);
            }
            else
            {
                tx.Put(KeySpace.AppliedBytes, changed);
            }
            return true;
        });
    }

    internal static void Repair(DocumentPatchAtomicityOwner fixture, byte[] original)
        => fixture.Store.Commit((tx, _) => { tx.Put(KeySpace.AppliedBytes, original); return true; });

    internal static string Detail(int kind) => kind == Truncated
        ? "The internal binary payload is invalid." : "The canonical applied position is invalid.";
}
