# StoryLens development instructions

- Read `CONTRACT.md` and `README.md` before changing behavior. Keep the internal name and assembly name `StoryLens`.
- Use the pinned `Dalamud.NET.Sdk/15.0.0` and committed lock files. Preserve the minimal standalone build; tests link the non-game `Core` sources directly.
- Prefer public Dalamud services, then SDK-supplied ClientStructs. Marshal native reads to the framework thread, copy managed values immediately, and never retain pointers across ticks.
- Never log authentication material, content/account IDs, full character identities or collected locations. Keep session-only players out of persisted data.
- Preserve combat/duty pause, request pacing/backoff, cancellation, clean unload and the enumerated read-only IPC smoke test. Debug defaults off.
- Add a small deterministic check for meaningful non-game logic changes. Build and run existing tests; do not claim live verification from compilation.
- Keep the behavioral contract and documentation accurate. Do not publish releases or submit to the official plugin repository without explicit maintainer authorization and personal validation.
- Public remote: https://github.com/Matuno/StoryLens. The sibling Dalamud workspace maintains the canonical live-development copy at `../Dalamud/projects/active/StoryLens`, registered as active for release/staging maintenance. Keep shared source, tests, manifest and behavioral documentation synchronized between these copies when changing either one; preserve this repository's standalone build setup and instructions.
- Before a user-requested GitHub push, review the shared-source diff and exclude player configuration, credentials, research fixtures, logs and build artifacts. Preserve the public Companion research summary in EVIDENCE.md rather than copying the workspace's full investigation history. Workspace maintenance leaves changes local for review; repository linkage does not authorize automatic commits, pushes or releases.
