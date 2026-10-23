using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal sealed class RecurringDueTurnState
{
    private const int TransferTurn = 2;
    private int next;
    private DueSweepCursor? recurring;
    private QueueDeadlineCursor? queue;
    private RemoteTransferPendingCursor? transfer;

    internal async Task RunAsync(IOptions<DueCoordinationOptions> options,
        Func<DueSweepCursor?, CancellationToken, Task<DueSweepCursor?>> runRecurring,
        Func<QueueDeadlineCursor?, CancellationToken, Task<QueueDeadlineCursor?>> runQueue,
        Func<RemoteTransferPendingCursor?, CancellationToken, Task<RemoteTransferPendingCursor?>> runTransfer,
        CancellationToken token)
    {
        if (next == 1 && options.Value.QueueDeadlinePrincipalId is null)
        {
            next = TransferTurn;
        }
        if (next == TransferTurn && options.Value.TransferCoordinatorPrincipalId is null)
        {
            next = 0;
        }
        var turn = next;
        next = turn == TransferTurn ? 0 : turn + 1;
        if (turn == 0)
        {
            recurring = await runRecurring(recurring, token).ConfigureAwait(true);
        }
        else if (turn == 1 && options.Value.QueueDeadlinePrincipalId is not null)
        {
            queue = await runQueue(queue, token).ConfigureAwait(true);
        }
        else if (turn == TransferTurn && options.Value.TransferCoordinatorPrincipalId is not null)
        {
            transfer = await runTransfer(transfer, token).ConfigureAwait(true);
        }
    }
}
