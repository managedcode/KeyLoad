using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class RemoteTransferPendingDiscovery
{
    private static readonly byte[] Prefix = KeyCodec.Encode(RemoteTransferProtocol.IntentSpace);

    internal static RemoteTransferPendingPage Read(DatabaseEngine database, string subject,
        RemoteTransferPendingCursor? supplied, CancellationToken token)
    {
        JsonData.Identifier(subject);
        using var budget = new RemoteTransferPendingBudget(database, token);
        budget.Check();
        var page = database.Store.Read(view => Scan(database, view, subject,
            RemoteTransferPendingCursorSelection.Match(database.Store.Identity, supplied), budget, token));
        budget.Check();
        budget.Result(page.Hint);
        return page;
    }

    private static RemoteTransferPendingPage Scan(DatabaseEngine database, IKeyValueView view, string subject,
        RemoteTransferPendingCursor cursor, RemoteTransferPendingBudget budget, CancellationToken token)
    {
        var upper = cursor.UpperKey;
        if (upper is null)
        {
            _ = view.VisitReverseRange(Prefix, RemoteTransferCoordinationProtocol.SingleRecord, (key, _) =>
            { budget.Check(); upper = DueWorkRecordDecoder.CopyKey(key); return false; },
                observer: budget.Observe, cancellationToken: token);
        }
        if (upper is null)
        { return new(null, cursor with { UpperKey = null, LastKey = null }); }
        if (budget.Remaining <= RemoteTransferCoordinationProtocol.SingleRecord)
        { return new(null, cursor with { UpperKey = upper }); }
        return ReadForward(database, view, subject, cursor with { UpperKey = upper }, budget, token);
    }

    private static RemoteTransferPendingPage ReadForward(DatabaseEngine database, IKeyValueView view, string subject,
        RemoteTransferPendingCursor cursor, RemoteTransferPendingBudget budget, CancellationToken token)
    {
        var last = cursor.LastKey;
        RemoteTransferCoordinationHint? hint = null;
        var reachedTail = false;
        var scanned = view.VisitRange(Prefix, budget.Remaining - RemoteTransferCoordinationProtocol.SingleRecord, (key, value) =>
        {
            budget.Check();
            if (key.SequenceCompareTo(cursor.UpperKey) > RemoteTransferCoordinationProtocol.FirstIndex)
            { reachedTail = true; return false; }
            last = DueWorkRecordDecoder.CopyKey(key);
            hint = database.ReadTransferCoordinationHint(budget.View(view), key, value, subject);
            reachedTail = key.SequenceEqual(cursor.UpperKey);
            return hint is null && !reachedTail;
        }, cursor.LastKey, observer: budget.Observe, cancellationToken: token);
        var completed = reachedTail || !scanned.HasMore && !scanned.StoppedByVisitor;
        return new(hint, completed ? cursor with { UpperKey = null, LastKey = null } : cursor with { LastKey = last });
    }
}
