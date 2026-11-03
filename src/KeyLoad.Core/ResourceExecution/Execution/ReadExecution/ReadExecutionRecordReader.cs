using KeyLoad.Storage;

namespace KeyLoad.Core.ResourceExecution.Execution;

/// <summary>Reuses one typed decoder callback for sequential point reads in a single read cut.</summary>
internal sealed class ReadExecutionRecordReader<T> where T : class
{
    private readonly ReadExecutionBudget budget;
    private readonly IKeyValueView view;
    private readonly StorageValueReader decode;
    private T? record;

    internal ReadExecutionRecordReader(ReadExecutionBudget budget, IKeyValueView view)
    {
        this.budget = budget;
        this.view = view;
        decode = Decode;
    }

    internal T? Read(byte[] key)
    {
        budget.Check();
        try
        {
            view.ReadValue(key, decode);
            budget.Check();
            return record;
        }
        finally
        {
            record = null;
        }
    }

    private void Decode(ReadOnlySpan<byte> bytes) => record = OrleansSerialization.Deserialize<T>(bytes);
}
