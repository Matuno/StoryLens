# Story Lens

**Status: active / maintained.** [Download version 1.0.1](https://github.com/Matuno/StoryLens/releases/tag/v1.0.1).

Quest objective overlays, session-only story-progress clues for visible players, and saved last-seen points for members you track. Version 1.0.1.0, API 15 / `Dalamud.NET.Sdk/15.0.0`. Maintained as an active workspace plugin with release/staging compatibility checks. Historical verification notes below retain their status at the time; the promotion record supersedes those incubator labels.

Public repository: [Matuno/StoryLens](https://github.com/Matuno/StoryLens). The canonical live-development copy is `projects/active/StoryLens` in the sibling Dalamud workspace. Future changes must synchronize shared source, tests, manifest and behavioral documentation before a user-requested push; this checkout retains its standalone build setup. Workspace maintenance leaves changes local for review. Player records, credentials, research fixtures and build artifacts stay outside the repository.

## Development review (2026-10-27)

Lodestone parsing runs off the game thread and unload asynchronously drains its cancellable worker. Profile/roster validation rejects wrong owners, duplicates and incomplete entries while preserving earlier evidence. Saved identifiers are culture-independent, and progress explanations use the client's quest names. The FC list draws visible rows only; marker labels and lookups avoid repeated work. Configuration schema and tracking behavior are unchanged; debug defaults off.

All 99 tests, release/staging builds and package checks pass. Installed-data checks pass in English, German, French and Japanese; the plugin's own UI labels remain English. The updated DLL passed exact-hash live reload, smoke tests, targeted log checks and FC-list/context-menu/selection UI checks. See [CONTRACT.md](CONTRACT.md) for evidence and remaining checks.

## Active promotion (2026-10-20)

Promoted at the maintainer's request following their in-game marker/inference feedback and the recorded candidate/path/duty UI checks. All 83 managed tests pass against release 15.0.3.8 and staging 15.0.3.8-6-g4ec141d51; the full three-project workspace maintenance check, package inspection and MCP host build pass. The 1.0 build reloaded from its active path with a matching DLL hash, a passing read-only smoke test and no targeted log errors. Debug is off. See [CONTRACT.md](CONTRACT.md) for exact evidence and remaining manual checks.

A clean standalone export also passed locked restores, Release packaging and all 83 tests independently of the workspace.

## Start using it

1. Download `latest.zip` from the [release page](https://github.com/Matuno/StoryLens/releases/latest) and extract it to a permanent local folder, or build from source below. In Dalamud's settings, add the full path to the extracted `StoryLens.dll` (or `bin/Release/StoryLens.dll` when building) as a **Dev Plugin Location**, enable it and load Story Lens. Keep the included manifest and deps file beside the DLL.
2. Open `/storylens`, or right-click a player and choose **Story Lens** to open **Follow player** on them. This works across standard player context menus that provide a valid name/home world, including players without observations yet. Repeated use keeps following that player; it does not toggle them off or add them to the saved list. Use **Evidence & tracking** to save them explicitly.
3. **Free Company** is the passive overview: search members and compare rough progress and last-observed times. Click a member to open their path.
4. **Follow player** starts with **Possible quests**: a scrollable list of observed MSQ and sidequest candidates, showing recent versus earlier clues, possible steps, observations, timestamps and how many quests share each clue. Click a candidate to center its path of three earlier/five upcoming quests; click it again to clear. Choose an FC member, tracked player or someone observed this session; **Monitor target** or **Add a player by name** also saves that player.
5. Click any quest card to preview its summary, numbered objectives, locations and associated duties. A possible current step is highlighted only when recent evidence and the player's visible position support it; otherwise the highlight is explicitly a **Last match**. Shared locations remain ambiguous. Browsing an adjacent quest keeps the chosen candidate's path. Duty names and their dungeon/trial/raid/quest-battle icons appear directly underneath each quest name, with relationship details on hover.
6. Click the selected quest again, **Clear quest**, or **Close details** to deselect it. It stays cleared until another selection or **Follow player's estimate** / **Resume following player**. **Clear player** clears both the player and quest selection. Search fields and player/world pickers also have clear choices.
7. **Quest browser** searches all quest names with two or more characters; an empty search lists quests in the current area. **Settings** contains observation, public-profile collection, world markers and debug diagnostics. Leave **Observe visible players** enabled to gather clues on spawned players while observation is available.

World markers use the installed game's MSQ, blue unlock or normal quest icons, plus the quest name and step count, for example **In the Dark of the Tower (3/6)**. Distances are hidden. Numbering matches the objective list, with accepting the quest counted as step 1 and text-only objectives included in the total. Locations shared by multiple steps show each applicable fraction. These numbers identify catalog steps, not confirmed player completion. Explicitly selecting a quest focuses its markers; clearing it restores all nearby quest types. Following a player does not narrow the world markers. **Show in-world quest icons** can hide all of them. They can mark an NPC location even when story phasing hides that NPC from your character. They do not spawn NPCs or alter the game's quest state.

Each objective shows its number and instructions on one line, with coordinates and the map/floor name below. Click the map icon beside a location to open the game map and set its flag there. Objectives without a known coordinate remain listed without a map button.

The quest browser defaults to MSQ. Turn off its MSQ filter to browse side quests. This filter affects only the browser list; world markers include all quest types unless a specific quest is explicitly selected. Automatic inference always considers both MSQ and side quests.

## Quest chain and duties

The chain is built from the installed game's quest prerequisites, with reverse links for upcoming quests. It includes quests without map coordinates. It walks up to three steps back and five forward within the same quest kind (MSQ or other). Side-quest prerequisites of an MSQ are listed separately. At branches or alternate quest versions, it shows the choices and stops the linear preview until you choose one. Missing links, chain ends and repeated links are shown explicitly. Chain entries are a preview, not a record of that player's completed or active quests.

Duties are linked through quest unlock data, prerequisite duties, numbered `INSTANCEDUNGEON` / `QUESTBATTLE` script references, and Duty Finder unlock criteria. Dungeon, trial and solo-duty names/types come from the installed sheets. A duty referenced more than once is listed once with its applicable relationships. Duties without a category label use **Duty**. A solo encounter without a named Duty Finder entry uses **Solo encounter**.

These are associations, not proof that every linked duty must be completed during that quest. Prerequisites may include alternatives. Unrecognized script symbols and missing sheet links can leave a duty unlisted; **No duty linked in quest data** does not guarantee the quest has no duty.

## What an estimate means

From strongest to weakest, evidence is:

- **Observed entering a cutscene nearby:** consecutive observations changed from a normal status to Viewing Cutscene, within one second, in the same area, map, instance and actor lifetime, without a position jump over three yalms.
- **Seen in a cutscene nearby (start unknown):** the status was already set, including when first seen after streaming, zoning or a pause.
- **Combat began nearby; cause unknown:** the public combat flag changed with continuous observations and no position jump. This does not identify a quest fight or who spawned a monster.
- **Spent at least 8 seconds nearby:** continuous observations remained in the objective's matching area.
- **Seen in combat nearby; cause unknown:** combat was already active; ranked alongside lingering. Combat observations alone do not advance the FC overview's MSQ percentage.
- **Seen near this objective:** a positional match alone.

Matching uses six yalms horizontally and four vertically. There are no percentage probabilities. Each clue includes the number of distinct quests matching its observation, including side quests, independent of which world markers are drawn. Multiple steps referencing one NPC remain possible steps of the same quest. Repeated samples do not accumulate independent votes. Clues from the last 20 minutes rank ahead of older clues; older clues remain available within the per-player capacity. Revisiting an objective after that window replaces its old clue with the latest observation.

Even a single catalog match is not proof of a quest, quest step, completion or first-time story progress. Replay, unrelated interaction, missing marker data, status masking and approximate coordinates can all mislead. The plugin cannot read another player's private quest log, distinguish New Game+ reliably, or identify the cutscene being played.

Quest details flag cutscene references when present in the static quest data, but cannot reliably assign them to an objective. An NPC coordinate cannot distinguish dialogue from a cutscene or a fight trigger. Missing references or no observed status change do not prove dialogue-only. Combat near a marker and nearby monsters do not establish quest ownership. The evidence panel explains these limits without treating guesses as confirmed progress.

## Local data and limits

- **Tracked players:** up to 32 chosen name/home-world pairs plus up to 512 verified FC members are saved locally, together with their last observed UTC time, territory/map/instance, world coordinates and up to 128 quest clues. Clue timestamps stay separate from the last sighting time. The saved point survives logout, plugin reload and game restart. Unknown/removed quest locations are retained as IDs but do not masquerade as current catalog data. No actor IDs or live cutscene state are restored.
- **Session-only players:** all other valid spawned players are sampled every 250ms, without the marker's 150-yalm distance cutoff. Their names, positions and clues stay in memory. They survive logout within the same running plugin session and are discarded on game exit or plugin unload/reload. The cache holds 4,096 players with up to 128 clues each; on overflow, the oldest untracked entry is evicted. Tracked entries are protected. The searchable session list shows up to 200 matches at once.
- Last-seen changes are checkpointed every 30 seconds and flushed on logout/unload. Adding/removing a tracked player and explicit setting changes save immediately. A crash can lose the latest checkpoint interval. Schema 1/2 configuration upgrades to schema 3 while preserving the existing list and preferences.
- **Stop tracking and delete saved point** removes that player's saved record; normal session observation continues. **Clear recorded evidence** clears their current saved/session evidence; an enabled observer can gather new clues immediately afterward. Pausing stops new collection and breaks observation continuity while retaining earlier data.
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

The interface takes inspiration from [Questionable](https://github.com/PunishXIV/Questionable)'s restrained theme and compact hierarchy, and [RotationSolver Reborn](https://github.com/FFXIV-CombatReborn/RotationSolverReborn)'s cards, badges and grouped navigation. StoryLens uses scoped ImGui styling and the existing Dalamud texture service; no extra UI dependency is required.

## Free Company progress

Open **Free Company** in `/storylens`. Automatic FC tracking is enabled by default for this requested feature. It discovers your company from the cached in-game list (with an exact Lodestone character/profile fallback), reads every public roster page, and saves member evidence automatically. The manual list remains separate. Session-only strangers remain memory-only.

- `ShB - 80% ~`: an observed quest candidate around 80% of the Shadowbringers launch quest catalog.
- `ShB - 100% +`: public collections/achievements establish at least completion of that launch story; patch progress may still be unknown.
- `ShB - 1% ?`: a supporting area clue, including exact territory IDs such as the Ocular. Returning to an area is possible at any later point.
- `Unknown`: no positive progression evidence. A level ceiling can still be shown without inventing a progress estimate.

Launch and patch stories have separate denominators (ARR/HW/SB/ShB/EW/DT). These are catalog estimates, not private quest-log reads; early ARR includes alternate/legacy quest routes. Click a member to follow their quest path; expand **Evidence & tracking** for the estimate explanation, highest-combat-job constraint and tracking controls. Raw profile status, check timestamp, cached FC location and the individual achievement/mount/minion source list are hidden. Public-page absence/private settings are never negative completion evidence.

Checks happen one page at a time, at least 5 seconds apart: four pages per stale member, roster every six hours, completed profile scans every three days. A 499-member first scan takes roughly 2.8 hours of active sweep time plus roster pages and any backoff. Combat and duties automatically pause dispatch, result processing and cached FC reads; the panel shows the reason. An already sent request may finish downloading, with its result held until the pause ends. The sweep resumes its saved place without a catch-up burst or resetting server backoff. The queue and per-member progress survive reloads; it resumes partial work instead of refreshing everyone again. Private sections returned as HTTP 403 with a matching character frame are recorded as unavailable and the queue continues. Rate limits/service failures back off and preserve prior evidence. All automatic network work can also be paused with the FC checkbox. Native cached locations do not refresh the game UI or request data from the game server.

**Debug diagnostics** shows aggregate request/failure/rule counts and the next request deadline; it defaults off. Clearing a member's evidence schedules fresh observation, so an enabled collector can refill it. Removing someone from the manual list does not stop their separate FC membership tracking. Complete verified roster updates remove departed members unless they remain manually tracked.

The global error retry is now **10 minutes**, or longer when Lodestone sends a longer `Retry-After`. The specific failed member retains its six-hour deferral so other members can proceed when the global pause ends. A deadline already saved by an older build is honored once because it does not record whether the server requested that delay.

Sources, rule exclusions and inference limits: [EVIDENCE.md](EVIDENCE.md).

### Free Company verification

The initial 0.2.0.0 FC extension passed 59 deterministic tests, locked release/staging builds with zero warnings/errors, and package inspection (plugin DLL, deps file and manifest only). Installed-game checks validated 1,050 MSQ records, 76 achievement links, seven direct reward rules and 240 area hints. Real public Lodestone pages passed roster pagination, maximum combat-level, owned achievement, both collection and exact character-search parsing.

Live validation confirmed exact DLL hash/MVID, clean reload, zero targeted errors, full configuration JSON round trip and all aggregate smoke invariants. All **499 currently public FC members** were imported; the in-game cache reported 503, and the UI explicitly shows this source discrepancy. Three existing manual entries survived migration. The background scan completed multiple four-page profiles and continued to further members; it preserved cursor/freshness/evidence across reloads. A real private-achievement HTTP 403 was reproduced, correctly recorded as unavailable after the fix, and the queue advanced without errors. All 200 names in the available native FC cache matched the imported roster; no missing identities were found in that slice. At the final reload there were three completed profiles, four with level/evidence data, 24 cached member locations and four saved world-observation points. Debug mode remains off.

The 2026-10-15 sweep-speed follow-up passed 63 tests, including combat/duty and manual/offline pause combinations. Work, locked release/staging and live builds passed without warnings/errors; the exact loaded DLL passed smoke and the targeted log check. Live diagnostics confirmed the five-second interval, preserved 499 members/16 completed profiles, and debug off. The first subsequent class/job request returned generic HTTP 403, correctly preserving evidence and scheduling a one-hour global retry. That server backoff was left intact, so sustained five-second throughput has not been demonstrated. Actual combat/duty transitions were not exercised in-game.

The subsequent retry-policy change passed 64 tests, including missing/expired/short/long server deadlines, locked release/staging builds and exact-DLL live validation with zero targeted errors. Live smoke reports a 10-minute default retry and five-second request spacing. The existing 15:49:23 UTC deadline remains honored; a new live failure/retry cycle was not forced. Scraper/API research is recorded in EVIDENCE.md; no new service was connected.

The initial sweep remains incomplete. No game restart, personal visual UI review or comparison against every member's actual quest log was performed. Old ARR catalog branches, stale/private profiles, replay and area-only evidence remain estimation limits. This check predates promotion to active status.

## Development and licensing

This project was developed with substantial OpenAI Codex assistance: the maintainer requested features, tested the initial markers and estimates, and requested active promotion, while Codex wrote most implementation, tests and documentation. Remaining personal validation is listed in [CONTRACT.md](CONTRACT.md). Official Dalamud plugin-repository submission requires a separate review under its current community policy.

Licensed under **AGPL-3.0-or-later**, matching the project's existing license declaration. See [LICENSE](LICENSE). Game data is read from the installed client; no game data, Companion APK, login helper, session credentials or collected player records are included.

## UI revision (2026-10-18)

Version 0.3.0.0 adds the FC overview, a player-focused quest path, quest summaries/objective text, associated duties, game quest icons and explicit deselection. The UI uses scoped dark slate/teal styling, cards and badges; debug remains off. Persistence, automatic collection and combat/duty pause behavior are unchanged.

All 68 managed tests pass, including clear/resume selection, pinned previews, current versus historical objective matches, shared-step ambiguity and future-evidence rejection. The actual catalog loaded all 5,373 quest detail sheets without exceptions; every location retained its objective index, all four resolved icon assets exist, and representative MSQ/unlock/side quest text plus Divine Intervention's known objective text passed checks. The work and standalone Release builds passed without warnings/errors. The standalone package was built outside OneDrive after its existing output directory refused removal; the source needed no build workaround. The final 0.3.0.0 live reload matched its built DLL hash/MVID (SHA-256 c3b6b3070b288372e817928601761ce1e8fcbbe05a5d9f8b444c89b7d7c0ba07), passed the read-only smoke test and had zero targeted log errors on Dalamud 15.0.3.8.

Visual checks in the running game covered the FC overview, opening a member's path, quest text and MSQ icons, independent quest previews, and clearing a preview by clicking it again. The empty details pane stayed cleared, with following available explicitly; nearby MSQ world icons returned. The user stopped Computer Use with Escape before the clear-player click could be verified. Final title-bar styling and automatic scrolling to the path anchor are loaded and pass smoke checks but have not been visually rechecked. A fresh observed player advancing through objectives, blue/normal world icons, every DPI setting and a game restart were not exercised. These checks predate the active promotion recorded above.

### Objective numbering and map links (2026-10-18)

The follow-up passes 71 managed tests, including shared-location numbering, acceptance/text-only steps and sparse metadata. Work and standalone Release builds pass without warnings/errors. The installed catalog confirms six displayed steps for In the Dark of the Tower. Exact-DLL live reload and read-only smoke pass with zero targeted log errors (SHA-256 68a6d029c188a208aece89147c6a14054f148a5d39bd33f9aa1d546443efda12). Running-game screenshots confirm numbered world labels with no distance. The revised objective rows and map-button click remain visually unverified because active user input prevented UI automation; cross-floor map navigation and other DPI settings were not exercised.

## Candidate uncertainty and duty cards (2026-10-20)

The top of Follow player now exposes recent and earlier quest candidates, their possible steps, supporting observations, timestamps and shared-quest counts. Explicit candidate selection centers that path and filters markers; toggling it off clears details and restores all markers. Nearby quest previews keep the chosen path. Duty names and content-type icons sit directly beneath quest names. Changing details returns to the summary/evidence. Cutscene references remain quest-wide, and combat observations never claim quest/monster ownership or independently advance the FC MSQ percentage. Saved/live clue merging now preserves stronger recent saved evidence across reloads instead of replacing it with a new proximity sample.

83 managed tests pass. The real catalog probe validated 5,373 quest detail sheets and 471 distinct linked duties, including all 16 duty icon assets, Sastasha's dungeon icon and Tower cutscene references. Debug/live and standalone Release builds pass without warnings/errors. Final exact-DLL live reload and read-only smoke pass on Dalamud 15.0.3.8 with no targeted log errors (SHA-256 945416780eb8317c57253706d588e43de283cc7cd42e2b7a12bcf4e62cafc963). The redacted report is `.workspace/storylens-uncertainty-validation.txt` in the development workspace.

Live visual checks covered alternative candidate selection, moving the path, clicking again to deselect, restored world markers, opening a new quest at its summary, and the Tower of Zot name/icon beneath its path card. Existing evidence showed shared cutscene locations as multiple alternatives. A controlled remote combat transition, quest-spawned monster attribution, every DPI/window size, and a full game restart were not exercised. Combat continuity and saved-evidence merging are covered by deterministic tests. Debug defaults off. These checks predate the active promotion recorded above.
