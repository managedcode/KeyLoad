namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal enum RequestCqrsAnnFault
{
    WrongIdentity, WrongSequence, WrongEvent, WrongMessage, UnexpectedPhase,
    MixedText, MixedOnline, MissingCompleted,
    ExtraCompleted, AfterFinal, FrameBudget
}
