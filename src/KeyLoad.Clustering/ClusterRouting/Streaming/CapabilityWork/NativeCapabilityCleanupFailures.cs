using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>Retains the four ordered cleanup failures possible for one capability settlement.</summary>
internal struct NativeCapabilityCleanupFailures
{
    private const string CleanupOverflowMessage = "Capability cleanup exceeded its fixed stages.";

    private Exception? first;
    private Exception? second;
    private Exception? third;
    private Exception? fourth;

    internal int Count
    {
        get; private set;
    }

    internal void Add(Exception error)
    {
        const int FirstCleanupFailureIndex = 0;
        const int SecondCleanupFailureIndex = 1;
        const int ThirdCleanupFailureIndex = 2;
        const int FourthCleanupFailureIndex = 3;

        switch (Count++)
        {
            case FirstCleanupFailureIndex:
                first = error;
                break;
            case SecondCleanupFailureIndex:
                second = error;
                break;
            case ThirdCleanupFailureIndex:
                third = error;
                break;
            case FourthCleanupFailureIndex:
                fourth = error;
                break;
            default:
                throw new InvalidOperationException(CleanupOverflowMessage);
        }
    }

    internal readonly Exception? FindFatal()
        => CqrsRuntimeFailures.FindFatal(first) ?? CqrsRuntimeFailures.FindFatal(second)
            ?? CqrsRuntimeFailures.FindFatal(third) ?? CqrsRuntimeFailures.FindFatal(fourth);

    internal readonly void AppendDistinct(Exception? primary, List<Exception> failures)
    {
        Append(first, primary, failures);
        Append(second, primary, failures);
        Append(third, primary, failures);
        Append(fourth, primary, failures);
    }

    private static void Append(Exception? error, Exception? primary, List<Exception> failures)
    {
        if (error is not null && !ReferenceEquals(error, primary))
        {
            failures.Add(error);
        }
    }
}
