using Microsoft.Extensions.Options;
namespace KeyLoad.Core.Features.GraphTraversal;

/// <summary>Reserves conservative in-memory metadata before path state is retained.</summary>
internal sealed class GraphPathRetention
{
    private readonly DatabaseLimits limits;
    private readonly ReadExecutionBudget budget;

    internal GraphPathRetention(IOptions<DatabaseLimits> options, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(options);
        limits = options.Value;
        limits.Validate();
        this.budget = budget;
    }
    private const long JsonMetadataMultiplier = 2;
    private const long FixedMetadataBytes = 128;
    private const string RetentionExceeded = "The graph path metadata budget was exhausted.";
    private const string ResultExceeded = "The graph path result byte budget was exhausted.";
    private long retainedBytes;
    private long resultBytes;

    internal void AdmitVertex(EntityRef vertex)
        => Reserve(budget.MeasureResult(vertex));

    internal void AdmitPredecessor(EntityRef vertex, GraphPathPredecessor predecessor)
        => Reserve(budget.MeasureResult(new GraphPathPredecessorMetadata(vertex, predecessor.Previous,
            predecessor.EdgeId)));

    internal void AdmitCollection(PartitionRef partition, string collection)
        => Reserve(budget.MeasureResult(new GraphPathCollectionDecision(partition, collection)));

    internal void AdmitResult(long exactJsonBytes)
    {
        budget.Check();
        if (exactJsonBytes > limits.MaxBatchBytes - resultBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResultExceeded);
        }
        resultBytes = checked(resultBytes + exactJsonBytes);
    }

    private void Reserve(long exactJsonBytes)
    {
        var amount = checked(checked(exactJsonBytes * JsonMetadataMultiplier) + FixedMetadataBytes);
        budget.Check();
        if (amount > limits.MaxBatchBytes - retainedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RetentionExceeded);
        }
        retainedBytes = checked(retainedBytes + amount);
    }
}
