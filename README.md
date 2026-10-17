# Story Lens

Quest objective overlays, session-only story-progress clues for visible players, and saved last-seen points for members you track. API 15 / `Dalamud.NET.Sdk/15.0.0`. Experimental; requires personal in-game validation before a supported release.

## Start using it

1. Build the plugin using the instructions below. In Dalamud's settings, add the full path to `bin/Release/StoryLens.dll` as a **Dev Plugin Location**, enable it and load Story Lens.
2. Open `/storylens`.
3. Leave **Automatically observe visible players** enabled. The plugin gathers clues on all player objects currently spawned in your client, while observation is available.
4. Target a member and choose **Add targeted player**, enter their full name/home world under **Add by name**, or select them under **Session-only players** and choose **Track player and save last-seen point**. Tracking adopts clues already gathered this session. Select a tracked or session-only player to inspect their last-seen position and quest evidence.
5. Select a candidate to focus its world markers. The **Quest objectives** tab also lets you search for quests independently of your own completed quest log. **Show all nearby quests again** clears the focus.
6. Open **Quest chain & duties** for three previous quests, the selected quest, and five upcoming quests, with associated duties under each entry. With no focused quest, it follows the selected member's strongest candidate. Select an entry to preview that quest, or choose **Follow selected player's strongest candidate** to resume following the estimate.

World markers show quest names and distances at the coordinates in the installed game's quest tables. Selecting a quest lists its objective numbers and NPC names. They can mark an NPC location even when story phasing hides that NPC from your character. They do not spawn NPCs or alter the game's quest state.

The nearby view defaults to MSQ. Turn off its MSQ filter to browse side quests. An explicitly focused quest shows its markers regardless of that browsing filter. Automatic inference always considers both MSQ and side quests.

## Quest chain and duties

The chain is built from the installed game's quest prerequisites, with reverse links for upcoming quests. It includes quests without map coordinates. It walks up to three steps back and five forward within the same quest kind (MSQ or other). Side-quest prerequisites of an MSQ are listed separately. At branches or alternate quest versions, it shows the choices and stops the linear preview until you choose one. Missing links, chain ends and repeated links are shown explicitly. Chain entries are a preview, not a record of that player's completed or active quests.

Duties are linked through quest unlock data, prerequisite duties, numbered `INSTANCEDUNGEON` / `QUESTBATTLE` script references, and Duty Finder unlock criteria. Dungeon, trial and solo-duty names/types come from the installed sheets. A duty referenced more than once is listed once with its applicable relationships. Duties without a category label use **Duty**. A solo encounter without a named Duty Finder entry uses **Solo encounter**.

These are associations, not proof that every linked duty must be completed during that quest. Prerequisites may include alternatives. Unrecognized script symbols and missing sheet links can leave a duty unlisted; **No duty linked in quest data** does not guarantee the quest has no duty.

## What an estimate means

From strongest to weakest, evidence is:

- **Observed entering a cutscene nearby:** consecutive observations changed from a normal status to Viewing Cutscene, within one second, in the same area, map, instance and actor lifetime, without a position jump over three yalms.
- **Seen in a cutscene nearby (start unknown):** the status was already set, including when first seen after streaming, zoning or a pause.
- **Spent at least 8 seconds nearby:** continuous observations remained in the objective's matching area.
- **Seen near this objective:** a positional match alone.

Matching uses six yalms horizontally and four vertically. The UI shows the three-dimensional distance at the observation. There are no percentage probabilities. Each clue includes the number of distinct quests matching its observation, including side quests. Multiple steps referencing one NPC remain possible steps of the same quest. Repeated samples do not accumulate independent votes. Clues from the last 20 minutes rank ahead of older clues; older clues remain available within the per-player capacity. Revisiting an objective after that window replaces its old clue with the latest observation.

Even a single catalog match is not proof of a quest, quest step, completion or first-time story progress. Replay, unrelated interaction, missing marker data, status masking and approximate coordinates can all mislead. The plugin cannot read another player's private quest log, distinguish New Game+ reliably, or identify the cutscene being played.

## Local data and limits

- **Tracked players:** up to 32 chosen name/home-world pairs plus up to 512 verified FC members are saved locally, together with their last observed UTC time, territory/map/instance, world coordinates and up to 128 quest clues. Clue timestamps stay separate from the last sighting time. The saved point survives logout, plugin reload and game restart. Unknown/removed quest locations are retained as IDs but do not masquerade as current catalog data. No actor IDs or live cutscene state are restored.
- **Session-only players:** all other valid spawned players are sampled every 250ms, without the marker's 150-yalm distance cutoff. Their names, positions and clues stay in memory. They survive logout within the same running plugin session and are discarded on game exit or plugin unload/reload. The cache holds 4,096 players with up to 128 clues each; on overflow, the oldest untracked entry is evicted. Tracked entries are protected. The searchable session list shows up to 200 matches at once.
- Last-seen changes are checkpointed every 30 seconds and flushed on logout/unload. Adding/removing a tracked player and explicit setting changes save immediately. A crash can lose the latest checkpoint interval. Schema 1/2 configuration upgrades to schema 3 while preserving the existing list and preferences.
- **Stop tracking (delete saved point)** removes that player's saved record; normal session observation continues. **Clear recorded data** clears their current saved/session evidence; an enabled observer can gather new clues immediately afterward. Pausing stops new collection and breaks observation continuity while retaining earlier data.
- Markers are limited to the current territory/map and 150 yalms, with at most 32 considered per frame. Nearby overlapping markers are suppressed. They can show through walls.
- Observation and markers pause during your cutscenes, zoning, duties, PvP and GPose. Moving between areas/instances or losing sight of a member breaks continuity, so the next sample cannot invent a cutscene start.
- Locations come from `Quest.IssuerLocation` and `Quest.TodoParams[].ToDoLocation[]` linked to `Level`. Missing, broad-area or script-only objectives may not have a useful coordinate. The catalog includes all named quests with known locations; not every location is currently obtainable.
- Names/identifiers/positions are not logged or returned by the read-only workspace smoke test. Free Company tracking adds paced public Lodestone GETs and read-only SDK ClientStructs access to the cached FC list. No content/account IDs, hooks, custom signatures, game input or gameplay actions are used.

If saving fails, the window shows **Retry save** and retries automatically after 30 seconds. Until saving succeeds, new changes are session-only. Invalid saved data fails validation rather than being overwritten. Only manual-list or verified FC-member records can pass persistence validation; the session cache is never part of the configuration.

## Build and test

Requires Windows, the .NET 10 SDK and a local Dalamud API 15 development distribution. The SDK uses `%APPDATA%\XIVLauncher\addon\Hooks\dev` by default; set `DALAMUD_HOME` to another compatible distribution directory if needed.

From this repository root:

```powershell
dotnet restore .\StoryLens.csproj --locked-mode
dotnet build .\StoryLens.csproj -c Release --no-restore
dotnet restore .\tests\StoryLens.Tests.csproj --locked-mode
dotnet test .\tests\StoryLens.Tests.csproj -c Release --no-restore
```

The plugin is written to `bin/Release/StoryLens.dll`; the packaged build is `bin/Release/StoryLens/latest.zip`. Tests exercise managed logic without a running game. Building does not verify in-game behavior. The original workspace's optional read-only IPC smoke test remains available, but its bridge and automation are not dependencies of this repository.

### Standalone export verified on 2026-10-17

Locked restore and Release build passed with zero warnings/errors; all 64 managed tests passed. The exported C# sources match the workspace version. No new in-game verification was performed for this source publication.

### Verification recorded on 2026-10-15

- The earlier persistence milestone passed 42 deterministic tests, including inference/chain behavior, schema migration, last-seen coordinate/time/clue round trips, rejection of untracked persisted records, session adoption, age-aware ranking, capacity eviction and missing catalog entries.
- Debug build against installed Dalamud, plus Release packages against exact release `15.0.3.7` and staging `15.0.3.7-4-g449cbb3c7`, passed without warnings.
- The actual catalog loader ran against the installed game files: 28,108 objective entries across 5,270 quests, including 1,050 MSQs; all entries passed managed coordinate/ID invariants. This is a data check outside the game process.
- Workspace active-project maintenance and bridge/MCP builds passed.
- The chain/duty loader was checked against all 5,373 named quest records, including quests without marker coordinates, and 639 duty associations. Every previous/upcoming context respected its limits and contained no repeated quest IDs. Known dungeon/trial/solo duty associations and all duty labels were checked.
- The user confirmed the original world markers and a correct rapid quest estimate in-game. The persistence/session update reloaded in the running dev client; the loaded assembly hash matched the built DLL, its read-only smoke test passed catalog/chain/observation/persistence and host JSON round-trip checks, and the targeted reload log contained zero errors. A nonempty fake last-seen point also round-tripped through the installed host JSON library with exact coordinates/time/clues and no actor state. Visual layout, end-to-end logout/game restart, every association and manual tracking/untracking still need personal checks. Runtime smoke checks do not establish those results.

## Inspiration and sources

[QuestShare](https://github.com/Era-FFXIV/QuestShare.Plugin) shares a consenting quest owner's actual quest state. Its use of quest TODO locations inspired this independent, local estimation approach; no QuestShare code or server dependency is bundled. Exact shared progress and observed clues are different kinds of evidence.

The SDK supplies Lumina, quest/Level schemas and public Dalamud services. Current public interfaces are available in the clean framework reference and the [Dalamud service documentation](https://dalamud.dev/api/Dalamud.Plugin.Services/).

## Free Company progress

Open **Free Company** in `/storylens`. Automatic FC tracking is enabled by default for this requested feature. It discovers your company from the cached in-game list (with an exact Lodestone character/profile fallback), reads every public roster page, and saves member evidence automatically. The manual list remains separate. Session-only strangers remain memory-only.

- `ShB - 80% ~`: an observed quest candidate around 80% of the Shadowbringers launch quest catalog.
- `ShB - 100% +`: public collections/achievements establish at least completion of that launch story; patch progress may still be unknown.
- `ShB - 1% ?`: a supporting area clue, including exact territory IDs such as the Ocular. Returning to an area is possible at any later point.
- `Unknown`: no positive progression evidence. A level ceiling can still be shown without inventing a progress estimate.

Launch and patch stories have separate denominators (ARR/HW/SB/ShB/EW/DT). These are catalog estimates, not private quest-log reads; early ARR includes alternate/legacy quest routes. Click a member for the evidence, highest-combat-job constraint, each public-page status, last check, cached FC location and a button to show the estimated quest's chain/duties/markers. Public-page absence/private settings are never negative completion evidence.

Checks happen one page at a time, at least 5 seconds apart: four pages per stale member, roster every six hours, completed profile scans every three days. A 499-member first scan takes roughly 2.8 hours of active sweep time plus roster pages and any backoff. Combat and duties automatically pause dispatch, result processing and cached FC reads; the panel shows the reason. An already sent request may finish downloading, with its result held until the pause ends. The sweep resumes its saved place without a catch-up burst or resetting server backoff. The queue and per-member progress survive reloads; it resumes partial work instead of refreshing everyone again. Private sections returned as HTTP 403 with a matching character frame are recorded as unavailable and the queue continues. Rate limits/service failures back off and preserve prior evidence. All automatic network work can also be paused with the FC checkbox. Native cached locations do not refresh the game UI or request data from the game server.

**Debug diagnostics** shows aggregate request/failure/rule counts and the next request deadline; it defaults off. Clearing a member's evidence schedules fresh observation, so an enabled collector can refill it. Removing someone from the manual list does not stop their separate FC membership tracking. Complete verified roster updates remove departed members unless they remain manually tracked.

The global error retry is now **10 minutes**, or longer when Lodestone sends a longer `Retry-After`. The specific failed member retains its six-hour deferral so other members can proceed when the global pause ends. A deadline already saved by an older build is honored once because it does not record whether the server requested that delay.

Sources, rule exclusions and inference limits: [EVIDENCE.md](EVIDENCE.md).

### Free Company verification

The initial 0.2.0.0 FC extension passed 59 deterministic tests, locked release/staging builds with zero warnings/errors, and package inspection (plugin DLL, deps file and manifest only). Installed-game checks validated 1,050 MSQ records, 76 achievement links, seven direct reward rules and 240 area hints. Real public Lodestone pages passed roster pagination, maximum combat-level, owned achievement, both collection and exact character-search parsing.

Live validation confirmed exact DLL hash/MVID, clean reload, zero targeted errors, full configuration JSON round trip and all aggregate smoke invariants. All **499 currently public FC members** were imported; the in-game cache reported 503, and the UI explicitly shows this source discrepancy. Three existing manual entries survived migration. The background scan completed multiple four-page profiles and continued to further members; it preserved cursor/freshness/evidence across reloads. A real private-achievement HTTP 403 was reproduced, correctly recorded as unavailable after the fix, and the queue advanced without errors. All 200 names in the available native FC cache matched the imported roster; no missing identities were found in that slice. At the final reload there were three completed profiles, four with level/evidence data, 24 cached member locations and four saved world-observation points. Debug mode remains off.

The 2026-10-15 sweep-speed follow-up passed 63 tests, including combat/duty and manual/offline pause combinations. Work, locked release/staging and live builds passed without warnings/errors; the exact loaded DLL passed smoke and the targeted log check. Live diagnostics confirmed the five-second interval, preserved 499 members/16 completed profiles, and debug off. The first subsequent class/job request returned generic HTTP 403, correctly preserving evidence and scheduling a one-hour global retry. That server backoff was left intact, so sustained five-second throughput has not been demonstrated. Actual combat/duty transitions were not exercised in-game.

The subsequent retry-policy change passed 64 tests, including missing/expired/short/long server deadlines, locked release/staging builds and exact-DLL live validation with zero targeted errors. Live smoke reports a 10-minute default retry and five-second request spacing. The existing 15:49:23 UTC deadline remains honored; a new live failure/retry cycle was not forced. Scraper/API research is recorded in EVIDENCE.md; no new service was connected.

The initial sweep remains incomplete. No game restart, personal visual UI review or comparison against every member's actual quest log was performed. Old ARR catalog branches, stale/private profiles, replay and area-only evidence remain estimation limits. The plugin remains an incubator.

## Development and licensing

This project was developed with substantial OpenAI Codex assistance: the maintainer requested features and tested the initial markers and estimates, while Codex wrote most implementation, tests and documentation. Further personal validation is listed in [CONTRACT.md](CONTRACT.md). This repository publishes experimental source; it is not an official Dalamud plugin-repository submission.

Licensed under **AGPL-3.0-or-later**, matching the project's existing license declaration. See [LICENSE](LICENSE). Game data is read from the installed client; no game data, Companion APK, login helper, session credentials or collected player records are included.
