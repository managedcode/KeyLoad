namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkPendingBoundProtocol
{
    internal const int Pending = 1;
    internal const int Windows = 2;
    internal const int WindowHours = 1;
    internal const long Appended = 2;
    internal const long Sealed = 3;
    internal const long Corrected = 4;
    internal const long Merged = 5;
    internal const long FirstSequence = 1;
    internal const long SecondSequence = 2;
    internal const long ThirdSequence = 3;
    internal const long FourthSequence = 4;
    internal const long Generation = 1;
    internal const long MergedGeneration = 2;
    internal const double FirstValue = 2;
    internal const double SecondValue = 4;
    internal const double LateValue = 6;
    internal const string FirstId = "pending-first";
    internal const string SecondId = "pending-second";
    internal const string FirstLateId = "pending-first-late";
    internal const string SecondLateId = "pending-second-late";
}
