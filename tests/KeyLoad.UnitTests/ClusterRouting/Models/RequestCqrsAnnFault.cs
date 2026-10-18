namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal enum RequestCqrsAnnFault
{
    WrongIdentity, WrongSequence, WrongEvent, WrongMessage, UnexpectedPhase,
    MixedText, MixedOnline, MixedTail, MissingCompleted,
    ExtraCompleted, AfterFinal, FrameBudget
}
