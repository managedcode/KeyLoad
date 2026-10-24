namespace KeyLoad.UnitTests.Authorization;

internal enum AuthorizationUnsafeSpanState
{
    None, Event, Link, ExcessTags, ExcessBaggage, InheritedParentBaggage, ExcessParentLinks, ClientEvent, ClientTaggedLink, ClientTraceStateLink, ClientExtraLink, ClientRemoteLink
}
