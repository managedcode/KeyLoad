using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorRowStorage
{
    private const string InvalidRow = "The native event vector owned row exceeds its admitted bound.";

    internal static T? Read<T>(IKeyValueView view, byte[] key, EventVectorAdmissionPolicy admission)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(admission);
        T? result = null;
        view.ReadValue(key, bytes =>
        {
            try
            { admission.RequireEncodedBytes(checked(key.LongLength + bytes.Length)); }
            catch (KeyLoadException error) when (error.Code == ErrorCode.ResourceExhausted)
            { throw Errors.Fail(ErrorCode.Corruption, InvalidRow, error); }
            result = NativeSerialization.Deserialize<T>(bytes);
        });
        return result;
    }

    internal static void Write(IAtomicTransaction transaction, EventVectorEncodedRow row)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        transaction.Put(row.Key.ToArray(), row.Value.ToArray());
    }
}
