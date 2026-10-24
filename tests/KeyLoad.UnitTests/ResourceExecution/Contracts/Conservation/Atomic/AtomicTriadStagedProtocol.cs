namespace KeyLoad.UnitTests.ResourceExecution;

internal static class AtomicTriadStagedProtocol
{
    internal const string Root = "root";
    internal const string Producer = "staged-producer";
    internal const string Collection = "staged_documents";
    internal const string Streams = "staged-events";
    internal const string Queue = "staged-jobs";
    internal const string Entity = "entity";
    internal const string FirstMessage = "first-message";
    internal const string SecondMessage = "second-message";
    internal const string FirstEvent = "first-event";
    internal const string SecondEvent = "second-event";
    internal const string FirstJson = "{\"number\":1}";
    internal const string SecondJson = "{\"number\":2}";
    internal const string Headers = "{\"stage\":true}";
    internal const string Created = "Created";
    internal const string Replaced = "Replaced";
    internal const string DuplicateDetail = "A message ID is already present in this lane.";
    internal const string PolicyDetail = "The principal policy changed since this command was evaluated.";
    internal const string ScopeDetail = "The principal cannot perform this operation in this scope.";
    internal const string Missing = "The genuine staged triad operation did not settle.";
    internal const string Index = "number";
    internal const string IndexField = "/number";
    internal const int ReplacementOrdinal = 3;
    internal const int MutationCount = 6;
    internal const int ColdCycles = 2;
    internal const long InitialEpoch = 1L;
    internal const long WithdrawnEpoch = 2L;
    internal const long RepairedEpoch = 3L;
    internal const long InitialRevision = 1L;
    internal const long FinalRevision = 2L;
}
