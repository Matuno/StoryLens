# Story Lens

<img src="assets/logo.png" alt="Story Lens: an open book and a quest path inside a lens" width="160" height="160">

A Dalamud plugin for getting a rough picture of your Free Company's story progress and following a player's likely MSQ or sidequest steps.

## What it does

- Shows FC members' estimated story progress and last-seen information.
- Lists possible quests for a player, their objectives, up to three previous quests, five upcoming quests and associated duties.
- Opens quest details and lets you mark objective locations on the game map.
- Draws nearby objectives in the world with MSQ, blue unlock or normal quest icons and step numbers, even when story phasing hides the NPC from you. Select a quest to focus its markers; clear the selection to show nearby quests again.

## How it works

Story Lens compares visible players' positions and activity with quest data from your game, then adds progression clues from public Lodestone profiles. Progress is an estimate: shared locations and revisits can leave several possible quests.

FC members and manually tracked players keep a local last-seen record. Observations of other visible players last only for the current session. Public-profile checks pause during combat and duties.

## Install

1. Open **Dalamud Settings → Experimental → Custom Plugin Repositories** and add:

   ```text
   https://raw.githubusercontent.com/Matuno/DalamudPlugins/main/repo.json
   ```

2. Enable the repository and save. Open **Plugin Installer**, search for **Story Lens**, and select **Install**.
3. Open `/storylens`, or right-click a player and choose **Story Lens**.

By Matuno. Developed with substantial OpenAI Codex assistance; logo generated with AI. [Build from source](CONTRIBUTING.md) · [AGPL-3.0 license](LICENSE).
