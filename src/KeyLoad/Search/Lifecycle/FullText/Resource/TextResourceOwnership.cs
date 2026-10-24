using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal sealed partial class TextResourceOwnership(IOptions<TextExecutionOptions> options)
{
    private const int NoFailures = 0;
    private const long EmptyFileLength = 0;
    private const int EmptyLeaseCount = 0;
    private readonly Lock gate = new();
    private readonly Dictionary<string, Action<ReadExecutionBudget?>> roots = new(StringComparer.Ordinal);
    private readonly HashSet<string> generations = new(StringComparer.Ordinal);
    private readonly Dictionary<object, TextResourceFileReservation> fileReservations = [];
    private readonly TextOpenFileGroups openFiles = new();
    private int leases;

    internal void RegisterRoot(string directory, Action<ReadExecutionBudget?> verify)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        lock (gate)
        {
            if (roots.TryGetValue(root, out var existingVerifier))
            {
                existingVerifier(null);
                verify(null);
                roots[root] = verify;
                Check(null);
                return;
            }
            if (roots.Keys.Any(existing => root.StartsWith(existing + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || existing.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            {
                throw TextErrors.Ownership();
            }
            verify(null);
            roots.Add(root, verify);
            var failures = new List<Exception>();
            ServerFailureObserver.Observe(() => Check(null), failures);
            if (failures.Count != NoFailures)
            {
                roots.Remove(root);
                ServerFailureObserver.ThrowIfAny(failures);
            }
        }
    }

    internal TextResourceReservation ReserveLease(ReadExecutionBudget? budget = null)
    {
        lock (gate)
        {
            budget?.Check();
            Check(budget);
            if (leases >= options.Value.MaximumActiveLeases)
            {
                throw TextErrors.Busy();
            }
            leases++;
            return new(this, null);
        }
    }

    internal TextResourceReservation ReserveGeneration(string directory, string leaf,
        ReadExecutionBudget budget)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        lock (gate)
        {
            budget.Check();
            if (!roots.ContainsKey(root) || !TextValidation.IsGenerationLeaf(leaf))
            {
                throw TextErrors.Ownership();
            }
            Check(budget);
            var path = Path.Combine(root, leaf);
            if (Directory.Exists(path) || File.Exists(path) || !generations.Add(path))
            {
                throw TextErrors.Ownership();
            }
            var failures = new List<Exception>();
            ServerFailureObserver.Observe(() => Check(budget), failures);
            if (failures.Count != NoFailures)
            {
                generations.Remove(path);
                ServerFailureObserver.ThrowIfAny(failures);
            }
            return new(this, path);
        }
    }

    internal TextResourceFileReservation ReserveFile(string path, long targetLength,
        Func<long> observeLength, TextOpenFileGroup? group = null)
    {
        var full = Path.GetFullPath(path);
        object key = group is null ? full : group;
        lock (gate)
        {
            if (targetLength < EmptyFileLength || fileReservations.ContainsKey(key)
                || !roots.Keys.Any(root => full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            {
                throw TextErrors.Ownership();
            }
            var reservation = new TextResourceFileReservation(this, key, full, targetLength, observeLength);
            fileReservations.Add(key, reservation);
            var failures = new List<Exception>();
            ServerFailureObserver.Observe(() => Check(null), failures);
            if (failures.Count != NoFailures)
            {
                fileReservations.Remove(key);
                ServerFailureObserver.ThrowIfAny(failures);
            }
            return reservation;
        }
    }

    internal void ReleaseFile(TextResourceFileReservation reservation)
    {
        lock (gate)
        {
            if (!fileReservations.TryGetValue(reservation.Key, out var original)
                || !ReferenceEquals(original, reservation))
            {
                throw TextErrors.Ownership();
            }
            Check(null);
            fileReservations.Remove(reservation.Key);
        }
    }

    internal T MutatePhysical<T>(Func<T> mutation, ReadExecutionBudget? budget = null)
    {
        lock (gate)
        {
            Check(budget);
            var failures = new List<Exception>();
            T result = default!;
            ServerFailureObserver.Observe(() => result = mutation(), failures);
            ServerFailureObserver.Observe(() => Check(null), failures);
            ServerFailureObserver.ThrowIfAny(failures);
            budget?.Check();
            return result;
        }
    }

    internal void MutatePhysical(Action mutation, ReadExecutionBudget? budget = null)
        => MutatePhysical(() => { mutation(); return true; }, budget);

    internal void Release(string? generation)
    {
        lock (gate)
        {
            if (generation is null)
            {
                if (leases <= EmptyLeaseCount)
                {
                    throw TextErrors.Ownership();
                }
                leases--;
            }
            else if (!generations.Remove(generation))
            {
                throw TextErrors.Ownership();
            }
        }
    }

    private void Check(ReadExecutionBudget? budget)
        => TextResourceRoot.Check(roots, generations, fileReservations, openFiles, budget, options);
}
