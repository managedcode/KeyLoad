using System.Runtime.InteropServices;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnReplayRecords
{
    private const int Empty = 0;
    private const long RowBytes = 64;

    internal static void ApplyVector(PutVector vector, MutationReceipt receipt, NativeAnnReplaySource initial,
        AnnSeedOptions options, AnnSeedWork work, Dictionary<string, VectorRecord> records,
        ref long retained, ref long peak, int dictionaryEntryBytes)
    {
        if (vector.Collection != initial.Scope.Collection || vector.Field != initial.Scope.Field)
        {
            return;
        }
        records.Remove(vector.Id);
        if (vector.Space != initial.Scope.Space)
        {
            return;
        }
        if (receipt.Resource != vector.Collection || receipt.Id != vector.Id
            || receipt.Revision != vector.ExpectedDocumentRevision || vector.Values.IsDefault
            || vector.Values.Length != vector.Space.Dimension)
        {
            throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt);
        }
        if (records.Count == options.MaxRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnnSeedAccounting.RecordLimit);
        }
        var bytes = checked(RowBytes + AnnSeedAccounting.StringAllowance(vector.Id)
            + AnnSeedAccounting.ArrayAllowance(sizeof(float), vector.Values.Length) + dictionaryEntryBytes);
        retained = checked(retained + bytes);
        Reserve(retained, ref retained, ref peak, options, work);
        var values = new float[vector.Values.Length];
        for (var index = Empty; index < values.Length; index++)
        {
            work.Charge();
            if (!float.IsFinite(vector.Values[index]))
            {
                throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt);
            }
            values[index] = vector.Values[index];
        }
        records.Add(vector.Id, new(new string(vector.Id.AsSpan()), initial.Scope.Field, initial.Scope.Space,
            ImmutableCollectionsMarshal.AsImmutableArray(values), receipt.Revision));
    }

    internal static void Reserve(long bytes, ref long retained, ref long peak, AnnSeedOptions options, AnnSeedWork work)
    {
        if (retained > options.MaxOwnedBytes || bytes > options.MaxPeakBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnnSeedAccounting.ByteLimit);
        }
        peak = Math.Max(peak, bytes);
        work.Check();
    }
}
