using KeyLoad.Core.Features.QueryExecution;
using KeyLoad.Query;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes TopicModelQueryRows operations against the caller's database context.</summary>
public static class TopicModelQueryRows
{
    internal static void VisitTopicModelRows(this DatabaseEngine engine, IKeyValueView view, PartitionRef partition, ResourceDefinition resource, ModelQuerySource modelSource, ReadExecutionBudget budget, bool explain, Action<DocumentRecord> accept)
    {
        ArgumentNullException.ThrowIfNull(accept);
        budget.Check();
        var budgetedView = budget.CreateView(view);
        var source = new EventSourceRef(partition, resource.Name, EventSourceKind.Topic, Generation: modelSource.Generation);
        var head = EventSources.SourceHead(budgetedView, source, resource);
        TopicModelQueryValidation.RequireHead(head, engine.Limits.MaxScanRecords);
        if (explain || head.FirstAvailablePosition > head.TailPosition)
        {
            return;
        }

        var position = head.FirstAvailablePosition;
        while (true)
        {
            budget.Check();
            var record = EventSources.SourceRecord(budgetedView, source, position);
            TopicModelQueryValidation.Accept(partition, resource, record, accept);
            if (position == head.TailPosition)
            {
                return;
            }

            position++;
        }
    }
}
