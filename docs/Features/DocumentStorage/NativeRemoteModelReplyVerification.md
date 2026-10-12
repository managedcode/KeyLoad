# Native remote-model reply verification ownership

Supplement to [Native remote-model transport](NativeRemoteModelTransport.md). This responsibility split preserves the selected native client and original verification; it introduces no wire contract, alias, service registration, resource owner or error policy.

| Requirement | Acceptance and source |
|---|---|
| REQ-NATIVE-MODEL-QUALITY-001: canonical aggregate type limit200 with file400/method64/nesting3 | AC-NATIVE-MODEL-QUALITY-001: NumericCodeLineCounter token/directive/disabled-text occupied lines count under limit; no partial or exception bypass. Source-equivalent measure and final owning compiler analyzer both required. |
| REQ-NATIVE-MODEL-QUALITY-002: original reply proof and terminal classification order unchanged | AC-NATIVE-MODEL-QUALITY-002: RemoteModelNativeClient.VerifyReplyAsync delegates same original borrowed fields to RemoteModelNativeReplyVerification.VerifyAsync; original body copy, outer/inner MAC, nonce, shape, expiry, current destination/caller and cancellation checks remain exact, including error/cause behavior. |
| REQ-NATIVE-MODEL-QUALITY-003: no lifetime/identity/wire drift | AC-NATIVE-MODEL-QUALITY-003: guarded client successor and fresh helper preserve READY115 disposal, replay/foreground uncertainty, Source7 same-sourceMac native borrow and original generated payloads; mechanical body equality/private apply proof, then actual Linux compilation and native whole RF3 qualification. |

Slice map: DocumentStorage/GrainServices owns the existing addressed native client and feature-local static reply-verification helper. The helper borrows dependencies from the client per invocation, creates/disposes the same original temporary target MAC and calls the same original current-target validator. It owns no service, key, admission or lifecycle instance. SDK/MCP/public JSON and all native aliases/IDs remain unchanged.

Compiler/analyzer/format/Linux, native callback/key lifetime, complete six-owner journal and TWOcold/RF3 gates remain OPEN until original owning qualification runs. Source-equivalent code-line measurement is not a compiler or runtime result.
