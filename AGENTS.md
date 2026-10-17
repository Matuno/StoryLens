# StoryLens development instructions

- Read `CONTRACT.md` and `README.md` before changing behavior. Keep the internal name and assembly name `StoryLens`.
- Use the pinned `Dalamud.NET.Sdk/15.0.0` and committed lock files. Preserve the minimal standalone build; tests link the non-game `Core` sources directly.
- Prefer public Dalamud services, then SDK-supplied ClientStructs. Marshal native reads to the framework thread, copy managed values immediately, and never retain pointers across ticks.
- Never log authentication material, content/account IDs, full character identities or collected locations. Keep session-only players out of persisted data.
- Preserve combat/duty pause, request pacing/backoff, cancellation, clean unload and the enumerated read-only IPC smoke test. Debug defaults off.
- Add a small deterministic check for meaningful non-game logic changes. Build and run existing tests; do not claim live verification from compilation.
- Keep the behavioral contract and documentation accurate. Do not publish releases or submit to the official plugin repository without explicit maintainer authorization and personal validation.
