using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal sealed class RecurringDueTurnState
{
    private const int RecurringTurn = 0;
    private const int QueueTurn = 1;
    private const int TransferTurn = 2;
    private int next = RecurringTurn;
    private DueSweepCursor? recurring;
    private QueueDeadlineCursor? queue;
    private RemoteTransferPendingCursor? transfer;

    internal async Task RunAsync(IOptions<DueCoordinationOptions> options,
        Func<DueSweepCursor?, CancellationToken, Task<DueSweepCursor?>> runRecurring,
        Func<QueueDeadlineCursor?, CancellationToken, Task<QueueDeadlineCursor?>> runQueue,
        Func<RemoteTransferPendingCursor?, CancellationToken, Task<RemoteTransferPendingCursor?>> runTransfer,
        CancellationToken token)
    {
        if (next == QueueTurn && options.Value.QueueDeadlinePrincipalId is null)
        { next = TransferTurn; }
        if (next == TransferTurn && options.Value.TransferCoordinatorPrincipalId is null)
        { next = RecurringTurn; }
        var turn = next;
        next = turn == TransferTurn ? RecurringTurn : turn + QueueTurn;
        if (turn == RecurringTurn)
        { recurring = await runRecurring(recurring, token).ConfigureAwait(true); }
        else if (turn == QueueTurn && options.Value.QueueDeadlinePrincipalId is not null)
        { queue = await runQueue(queue, token).ConfigureAwait(true); }
        else if (turn == TransferTurn && options.Value.TransferCoordinatorPrincipalId is not null)
        { transfer = await runTransfer(transfer, token).ConfigureAwait(true); }
    }
}
