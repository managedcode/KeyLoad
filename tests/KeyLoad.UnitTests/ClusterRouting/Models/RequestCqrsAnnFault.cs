namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal enum RequestCqrsAnnFault
{
    WrongIdentity, WrongSequence, WrongEvent, WrongMessage, UnexpectedPhase,
    MixedText, MixedMove, MixedOnline, MixedTail, MissingCompleted,
    ExtraCompleted, AfterFinal, FrameBudget
}
