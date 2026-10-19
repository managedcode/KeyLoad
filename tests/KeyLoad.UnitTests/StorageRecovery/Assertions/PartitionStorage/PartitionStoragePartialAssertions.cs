using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStoragePartialAssertions
{
    private const int NoFailures = 0;

    internal static async Task RequireAsync(PartitionStoragePartialStart observation,
        PartitionHostApplicationOwner owner, Task originalStart, IReadOnlyList<Exception> failures)
    {
        await Assert.That(originalStart.IsFaulted).IsTrue();
        await Assert.That(observation.Opening is { IsCompletedSuccessfully: true }).IsTrue();
        await Assert.That(ReferenceEquals(observation.Opening, owner.Opening)).IsTrue();
        await Assert.That(observation.PublicationWait is { IsCompletedSuccessfully: true }).IsTrue();
        await Assert.That(observation.Callback is { IsFaulted: true }).IsTrue();
        await Assert.That(observation.UnexpectedAcquisition).IsFalse();
        await Assert.That(observation.Identity is not null).IsTrue();
        await Assert.That(observation.Refusal is not null).IsTrue();
        await Assert.That(observation.Before is not null && observation.After is not null).IsTrue();
        await PartitionStorageImage.RequireAsync(observation.Before!, observation.After!).ConfigureAwait(false);
        var expected = observation.Refusal!;
        await Assert.That(failures.Count > NoFailures).IsTrue();
        foreach (var failure in failures)
        {
            await Assert.That(OnlyOriginal(failure, expected)).IsTrue();
        }
        await Assert.That(OnlyOriginal(observation.Callback!.Exception!, expected)).IsTrue();
    }

    private static bool OnlyOriginal(Exception failure, Exception expected)
    {
        if (ReferenceEquals(failure, expected))
        {
            return true;
        }
        if (failure is AggregateException aggregate)
        {
            return aggregate.InnerExceptions.Count > NoFailures
                && aggregate.InnerExceptions.All(inner => OnlyOriginal(inner, expected));
        }
        return failure.InnerException is { } inner && OnlyOriginal(inner, expected);
    }
}
