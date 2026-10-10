using KeyLoad.Core.Features.QueryExecution;
using KeyLoad.Query;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void VisitTopicModelRows(IKeyValueView view, PartitionRef partition, ResourceDefinition resource,
        ModelQuerySource modelSource, ReadExecutionBudget budget, bool explain, Action<DocumentRecord> accept)
    {
        ArgumentNullException.ThrowIfNull(accept);
        budget.Check();
        var budgetedView = budget.CreateView(view);
        var source = new EventSourceRef(partition, resource.Name, EventSourceKind.Topic, Generation: modelSource.Generation);
        var head = SourceHead(budgetedView, source, resource);
        TopicModelQueryValidation.RequireHead(head, Limits.MaxScanRecords);
        if (explain || head.FirstAvailablePosition > head.TailPosition)
        { return; }
        var position = head.FirstAvailablePosition;
        while (true)
        {
            budget.Check();
            var record = SourceRecord(budgetedView, source, position);
            TopicModelQueryValidation.Accept(partition, resource, record, accept);
            if (position == head.TailPosition)
            { return; }
            position++;
        }
    }
}
