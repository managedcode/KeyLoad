using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Replication;

/// <summary>Retains one incoming forward through its exact queued command and receiver publication.</summary>
internal sealed class ReplicaForwardedRequestExecution
{
    private Func<DatabaseEngine, TimeProvider, CancellationToken, Task<IReplicaForwardedCommandWork>>? factory;

    internal void Attach(Func<DatabaseEngine, TimeProvider, CancellationToken, Task<IReplicaForwardedCommandWork>> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Interlocked.CompareExchange(ref factory, value, null) is not null)
        {
            throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.InvalidPeer);
        }
    }

    internal bool IsAttached => Volatile.Read(ref factory) is not null;

    internal void RequireLowerAdmission()
    {
        if (Volatile.Read(ref factory) is not null)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.InvalidPeer);
        }
    }

    internal Task<T> ExecuteAuthenticatedAsync<T>(DatabaseEngine database, TimeProvider clock,
        ReplicaRequestDispatcher dispatcher, ReadOnlyMemory<byte> payload,
        Func<ReadOnlyMemory<byte>, Exception?, T> createReply, Func<T, long> measureReply,
        Action<T> completed, CancellationToken cancellationToken)
    {
        var frame = ReplicaForwardReplyEncoding.ReadRequest(payload, database);
        var create = Volatile.Read(ref factory)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.InvalidPeer);
        return ExecuteOwnedAsync(database, clock, dispatcher, frame, create,
            createReply, measureReply, completed, cancellationToken);
    }

    internal async Task<ReadOnlyMemory<byte>> ExecuteAsync(DatabaseEngine database, TimeProvider clock,
        ReplicaRequestDispatcher dispatcher, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var frame = ReplicaForwardReplyEncoding.ReadRequest(payload, database);
        var create = Volatile.Read(ref factory);
        if (create is null)
        {
            if (frame.Grant is not null)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.InvalidPeer);
            }
            var result = await dispatcher.HandleForwardAsync(frame, null, cancellationToken).ConfigureAwait(false);
            try
            {
                return ReplicaForwardReplyEncoding.Encode(frame, result, null, null);
            }
            finally
            {
                ReplicaForwardReplyEncoding.Clear(result);
            }
        }
        return await ExecuteOwnedAsync(database, clock, dispatcher, frame, create,
            RequireResult, static result => result.Length, IgnoreResult, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> ExecuteOwnedAsync<T>(DatabaseEngine database, TimeProvider clock,
        ReplicaRequestDispatcher dispatcher, ReplicaForwardRequestV1 frame,
        Func<DatabaseEngine, TimeProvider, CancellationToken, Task<IReplicaForwardedCommandWork>> create,
        Func<ReadOnlyMemory<byte>, Exception?, T> createReply, Func<T, long> measureReply,
        Action<T> completed, CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> encoded = default;
        try
        {
            return await ReplicaForwardRequestLifetime.ExecuteAsync(frame.ExpiresAt, clock,
                async token =>
                {
                    var work = await create(database, clock, token).ConfigureAwait(false)
                        ?? throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.InvalidPeer);
                    return await ReplicaForwardedExchangeExecution.ExecuteAsync(work, dispatcher, frame,
                        (payload, error) =>
                        {
                            encoded = payload;
                            return createReply(payload, error);
                        }, measureReply, completed).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            ReplicaForwardReplyEncoding.Clear(encoded);
            throw;
        }
    }

    private static void IgnoreResult(ReadOnlyMemory<byte> result) => _ = result;

    private static ReadOnlyMemory<byte> RequireResult(ReadOnlyMemory<byte> result, Exception? error)
    {
        if (error is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
        return result;
    }
}
