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
    private readonly Dictionary<object, TextResourceFileReservation> /* KLD0041: One reservation ledger preserves string value equality for canonical paths and reference identity for TextOpenFileGroup instances; both keys are existing references. */ fileReservations = ([] /* KLD0041: Initialize the single reservation ledger for canonical path value keys and shared open-file group reference keys; avoid an extra wrapper per reservation. */);
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
            if (roots.Keys.Any(existing => root.StartsWith(string.Concat(existing.AsSpan(), [Path.DirectorySeparatorChar]), StringComparison.Ordinal)
                || existing.StartsWith(string.Concat(root.AsSpan(), [Path.DirectorySeparatorChar]), StringComparison.Ordinal)))
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
        object /* KLD0041: One reservation ledger preserves string value equality for canonical paths and reference identity for TextOpenFileGroup instances; both keys are existing references. */ key = group is null ? full : group;
        lock (gate)
        {
            if (targetLength < EmptyFileLength || ((fileReservations /* KLD0041: Access the single ledger whose keys are canonical path strings or existing shared open-file group references; their distinct equality semantics are required. */).ContainsKey(key) /* KLD0041: Reject a second reservation for the same canonical path value or existing shared open-file group identity before effects. */)
                || !roots.Keys.Any(root => full.StartsWith(string.Concat(root.AsSpan(), [Path.DirectorySeparatorChar]), StringComparison.Ordinal)))
            {
                throw TextErrors.Ownership();
            }
            var reservation = (new TextResourceFileReservation(this, key, full, targetLength, observeLength) /* KLD0041: Retain the original canonical path string or shared open-file group reference as this reservation's accounting identity, without another allocation. */);
            (fileReservations /* KLD0041: Access the single ledger whose keys are canonical path strings or existing shared open-file group references; their distinct equality semantics are required. */).Add(key, reservation) /* KLD0041: Register the original path/group key in the single physical reservation ledger; no scalar value is boxed. */;
            var failures = new List<Exception>();
            ServerFailureObserver.Observe(() => Check(null), failures);
            if (failures.Count != NoFailures)
            {
                (fileReservations /* KLD0041: Access the single ledger whose keys are canonical path strings or existing shared open-file group references; their distinct equality semantics are required. */).Remove(key) /* KLD0041: Remove the original path/group reservation from the single physical accounting ledger after its owned work settles. */;
                ServerFailureObserver.ThrowIfAny(failures);
            }
            return reservation;
        }
    }

    internal void ReleaseFile(TextResourceFileReservation reservation)
    {
        lock (gate)
        {
            if (!((fileReservations /* KLD0041: Access the single ledger whose keys are canonical path strings or existing shared open-file group references; their distinct equality semantics are required. */).TryGetValue((reservation.Key /* KLD0041: This accounting key is either a canonical path string or the existing TextOpenFileGroup reference; distinguish shared open-file accounting without adding a key wrapper. */), out var original) /* KLD0041: Locate the reservation using its canonical path value or existing shared open-file group identity before release or byte accounting. */)
                || !(ReferenceEquals(original, reservation) /* KLD0041: Release only the exact registered file reservation instance through CLR reference identity. */))
            {
                throw TextErrors.Ownership();
            }
            Check(null);
            (fileReservations /* KLD0041: Access the single ledger whose keys are canonical path strings or existing shared open-file group references; their distinct equality semantics are required. */).Remove((reservation.Key /* KLD0041: This accounting key is either a canonical path string or the existing TextOpenFileGroup reference; distinguish shared open-file accounting without adding a key wrapper. */)) /* KLD0041: Remove the original path/group reservation from the single physical accounting ledger after its owned work settles. */;
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
        => TextResourceRoot.Check(roots, generations, (fileReservations /* KLD0041: Access the single ledger whose keys are canonical path strings or existing shared open-file group references; their distinct equality semantics are required. */), openFiles, budget, options) /* KLD0041: Validate the same mixed path/group reservation ledger together with roots and open-file groups so each physical file is counted once. */;
}
