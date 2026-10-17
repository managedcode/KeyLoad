namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnSeedCancellationFailureBag
{
    private readonly System.Threading.Lock gate = new();
    private readonly List<Exception> failures = [];

    internal void Add(Exception failure)
    {
        lock (gate)
        {
            if (!failures.Contains(failure, ReferenceEqualityComparer.Instance))
            {
                failures.Add(failure);
            }
        }
    }

    internal void CopyTo(List<Exception> destination)
    {
        lock (gate)
        {
            foreach (var failure in failures)
            {
                if (!destination.Contains(failure, ReferenceEqualityComparer.Instance))
                {
                    destination.Add(failure);
                }
            }
        }
    }
}
