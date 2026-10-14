# Plugin permission contract

Requirements for future permission-bearing plugins (not implemented in core):

- Request OS consent only when the feature is invoked and only for permissions
  that platform actually requires. Private app storage and ordinary audio playback
  must not trigger invented permission prompts.
- Report granted, denied, restricted, unavailable and settings-required outcomes
  explicitly. Denial is a normal result, not an unhandled exception or process abort.
- Recheck access when starting an operation. Permissions can change outside the
  application; an earlier grant does not guarantee the next operation succeeds.
- Handle cancellation, device unavailability and OS errors without crashing.
- Use the OS picker for user-selected files and honor its URI/security-scope rules;
  do not assume permission to access arbitrary filesystem paths.
- Supply required platform declarations through plugin build integration. A missing
  required declaration should fail validation before invoking a protected OS API.
- Never request broader access as an automatic fallback after denial.

Persistence remains an optional plugin. WidgetSamples startup does not require
filesystem access or permission grants.
