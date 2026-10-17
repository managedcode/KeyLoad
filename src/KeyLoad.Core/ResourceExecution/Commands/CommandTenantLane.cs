using System.Diagnostics.CodeAnalysis;

namespace KeyLoad.Core;

/// <summary>Owns bounded tenant FIFO turns under the existing inbox lock.</summary>
internal sealed class CommandTenantLane
{
    private const int EmptyElementCount = 0;

    private readonly Dictionary<string, Queue<AdmittedCommand>> tenants = new(StringComparer.Ordinal);
    private readonly Queue<string> turns = new();

    /// <summary>Gets the number of nonempty tenant queues.</summary>
    internal int Count => tenants.Count;

    /// <summary>Adds work in its authenticated tenant's FIFO.</summary>
    internal void Enqueue(AdmittedCommand command)
    {
        if (!tenants.TryGetValue(command.Tenant, out var queue))
        {
            queue = new();
            tenants.Add(command.Tenant, queue);
            turns.Enqueue(command.Tenant);
        }

        queue.Enqueue(command);
    }

    /// <summary>Takes one command and rotates only a nonempty tenant owner.</summary>
    internal AdmittedCommand Dequeue()
    {
        var tenant = turns.Dequeue();
        var queue = tenants[tenant];
        var command = queue.Dequeue();
        command.TransferFromQueue();
        if (queue.Count == EmptyElementCount)
        {
            tenants.Remove(tenant);
        }
        else
        {
            turns.Enqueue(tenant);
        }

        return command;
    }

    /// <summary>Takes a queued command while draining existing ownership.</summary>
    internal bool TryDequeue([NotNullWhen(true)] out AdmittedCommand? command)
    {
        if (Count == EmptyElementCount)
        {
            command = null;
            return false;
        }

        command = Dequeue();
        return true;
    }
}
