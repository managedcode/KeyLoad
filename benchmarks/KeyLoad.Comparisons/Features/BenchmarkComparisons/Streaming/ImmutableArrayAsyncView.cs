using System.Collections.Immutable;
using System.Text.Json;

namespace KeyLoad.Comparisons;

/// <summary>Exposes an immutable array through a repeatable, allocation-bounded async enumerable.</summary>
internal static class ImmutableArrayAsyncView
{
    internal const string InvalidCollectionMessage = "A required collection must be an initialized JSON array.";

    internal static IAsyncEnumerable<T> Create<T>(ImmutableArray<T> values) => Create(values, static value => value);

    internal static IAsyncEnumerable<TResult> Create<T, TResult>(ImmutableArray<T> values, Func<T, TResult> project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (values.IsDefault)
        {
            throw new JsonException(InvalidCollectionMessage);
        }

        return new View<T, TResult>(values, project);
    }

    private sealed class View<TSource, TResult>(ImmutableArray<TSource> values, Func<TSource, TResult> project)
        : IAsyncEnumerable<TResult>
    {
        public IAsyncEnumerator<TResult> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new Enumerator<TSource, TResult>(values, project, cancellationToken);
    }

    private sealed class Enumerator<TSource, TResult>(ImmutableArray<TSource> values, Func<TSource, TResult> project,
        CancellationToken cancellationToken) : IAsyncEnumerator<TResult>
    {
        private const int MissingItemIndex = -1;

        private int _index = MissingItemIndex;
        private TResult? _current;

        public TResult Current => _current!;

        public ValueTask<bool> MoveNextAsync()
        {
            const int AdjacentElementOffset = 1;

            cancellationToken.ThrowIfCancellationRequested();
            var next = _index + AdjacentElementOffset;
            if (next >= values.Length)
            {
                return ValueTask.FromResult(false);
            }

            _index = next;
            _current = project(values[next]);
            return ValueTask.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
