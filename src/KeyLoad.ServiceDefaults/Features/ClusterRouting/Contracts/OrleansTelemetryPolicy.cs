namespace KeyLoad.ServiceDefaults.Features.ClusterRouting.Contracts;

/// <summary>Closed source, tag, event, and suppression policy for native Orleans telemetry.</summary>
internal static class OrleansTelemetryPolicy
{
    internal const string OrleansMeterName = "Microsoft.Orleans";
    internal const string ApplicationActivitySourceName = "Microsoft.Orleans.Application";
    internal const string LifecycleActivitySourceName = "Microsoft.Orleans.Lifecycle";
    internal const string PrivacyMeterName = "KeyLoad.ClusterRouting.TelemetryPrivacy";
    internal const string SuppressedSpanCounterName = "keyload.orleans.suppressed_spans";
    internal const string SuppressionReasonTag = "reason";
    internal const string ApplicationDisplayName = "orleans.application.operation";
    internal const string LifecycleDisplayName = "orleans.lifecycle.operation";
    internal const string RpcSystemTag = "rpc.system.name";
    internal const string RpcSystemValue = "orleans";
    internal const string RpcServiceTag = "orleans.rpc.service";
    internal const string RpcServiceValue = "grain";
    internal const string RpcMethodTag = "rpc.method";
    internal const string GenericMethodValue = "grain.invoke";
    internal const string RequestMethodValue = "request.execute";
    internal const string CapabilityMethodValue = "capability.execute";
    internal const string RequestStreamMethodName = "ExecuteStreamAsync";
    internal const string CapabilityMethodName = "ExecuteAsync";
    internal const string MethodNameSeparator = "/";
    internal const string GrainTypeTag = "orleans.grain.type";
    internal const string GrainTypeValue = "grain";
    internal const string ActivationCauseTag = "orleans.activation.cause";
    internal const string ActivationCauseNewValue = "new";
    internal const string ActivationCauseRehydrateValue = "rehydrate";
    internal const string ExceptionTypeTag = "exception.type";
    internal const string ExceptionTypeValue = "exception";
    internal const string ExceptionEscapedTag = "exception.escaped";
    internal const string SuppressionTagLimit = "tag_limit";
    internal const string SuppressionEventLimit = "event_limit";
    internal const string SuppressionBaggageLimit = "baggage_limit";
    internal const string SuppressionLinksPresent = "links_present";
    internal const string SuppressionUnsafeEvent = "unsafe_event";

    internal const string EventInstanceCreated = "instance-created";
    internal const string EventRehydrated = "rehydrated";
    internal const string EventActivationStart = "activation-start";
    internal const string EventDirectoryRetryRecovery = "directory-register-retry-recovery";
    internal const string EventRetryRecovery = "retry-recovery";
    internal const string EventDirectoryRegisterSuccess = "directory-register-success";
    internal const string EventSuccess = "success";
    internal const string EventDirectoryRetryPrevious = "directory-register-retry-previous";
    internal const string EventRetryPrevious = "retry-previous";
    internal const string EventDuplicateActivation = "duplicate-activation";
    internal const string EventDuplicate = "duplicate";
    internal const string EventDirectoryRegisterFailed = "directory-register-failed";
    internal const string EventStateActivating = "state-activating";
    internal const string EventLifecycleStart = "lifecycle-start";
    internal const string EventLifecycleStarted = "lifecycle-started";
    internal const string EventLifecycleStartFailed = "lifecycle-start-failed";
    internal const string EventStateValid = "state-valid";

}
