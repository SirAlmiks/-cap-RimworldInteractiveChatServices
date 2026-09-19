// VersionHistory.cs 
// Copyright (c) Captolamia
// This file is part of CAP Chat Interactive.
// 
// CAP Chat Interactive is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// CAP Chat Interactive is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Affero General Public License for more details.
// 
// You should have received a copy of the GNU Affero General Public License
// along with CAP Chat Interactive. If not, see <https://www.gnu.org/licenses/>.

// File contains version history notes for RICS (RimWorld Interactive Chat Services) mod.
// Older entries (1.40-1.49) live in git history at 7db771e if this file was trimmed.

using System;
using System.Collections.Generic;
using Verse;

namespace CAP_ChatInteractive
{
    public static class VersionHistory
    {
        public static Dictionary<string, string> UpdateNotes = new Dictionary<string, string>
        {
            {"1.50",
@"===========================================================
                         RICS version 1.50 - Changelog
                         Pre-Release
===========================================================

<b>MEMORANDUM</b>
-----------------
- Chat moderators can silence a viewer inside RICS without touching Twitch, Kick, or YouTube. The person can still talk on the platform; RICS just ignores their commands.
- RimWorld XML sometimes fails to save large mod settings. RICS now keeps a JSON backup and can restore from it.
- !rescueme captured rescue sites now present a real Rescue option (pawn is downed and no longer stuck as a walking player-faction prisoner).

<b>FIXED</b>
-----------
- Can no longer purchase pawn if xenotype does not exist. Must put in proper xenotype or leave blank for Baseliner.
- Chat now shows the coin price on purchase success messages (it was dropping out of the sentence).
- Buying a xenotype that is not on your allowed list no longer quietly gives a normal human. The buy fails instead.
- If a new colonist would arrive with a missing or broken head, the purchase is cancelled and coins are not taken.
- Psytrainers can only be used by actual psycasters. Skill neurotrainers and psychic amplifiers still work for everyone.
- Skill neurotrainers cannot be used with !use if the pawn cannot use that skill (coins are not taken).
- People using Vanilla Psycasts Expanded are treated as psycasters, so Psytrainer checks work for them.
- !rescueme captured sites now down the pawn and clear player-faction prisoner status so the vanilla Rescue option appears. Healthy walking prisoners in the cell had no Rescue and no way back into the colony (had to Capture + Recruit).

<b>UPDATED</b>
--------------
- Settings window close now writes XML and a JSON backup. Save Backup still works. Only the 5 newest timestamped backups are kept.
- If XML and the latest JSON backup do not match on load, you are asked to Review settings or Load backup.
- Removed the general dev bypass of disabled commands from release. Captolamia can still run disabled commands on his own channel. On other streams only !captolamia runs for him (version / identity check). Everyone else still gets a RICS tip when that command is enabled.

<b>ADDED</b>
------------
- !rban user — permanent RICS-only ban (aliases: !ricsban).
- !runban user — clear RICS ban and timeout (aliases: !ricsunban).
- !rto user [duration] — timed RICS-only silence, wall clock not game ticks (aliases: !ricstimout, !rtimeout). Default 5 minutes. Examples: !rto bob 10, !rto bob 10m.
- Viewer Manager: timed-out viewers show amber and remaining time; Unban also clears timeout.
- Global option: Load settings from latest JSON backup on startup (overwrites XML after RimWorld loads it).

<b>TRANSLATIONS</b>
-------------------
- Keys for !rban / !runban / !rto and Viewer Manager timeout labels.
- Keys for JSON settings load option and mismatch dialog.
- Keys for !use skill neurotrainer blocked when the pawn cannot use that skill.
"
            }
        };

        public static void CheckForVersionUpdate()
        {
            var mod = CAPChatInteractiveMod.Instance;
            if (mod == null)
            {
                Logger.Error("Cannot check version update - CAPChatInteractiveMod.Instance is null");
                return;
            }

            var settingsContainer = mod.Settings;
            if (settingsContainer == null)
            {
                Logger.Error("Cannot check version update - mod Settings container is null");
                return;
            }

            var globalSettings = settingsContainer.GlobalSettings;
            if (globalSettings == null)
            {
                Logger.Error("Cannot check version update - GlobalSettings is null");
                return;
            }

            string currentVersion = globalSettings.modVersion ?? "Unknown";
            string savedVersion = globalSettings.modVersionSaved;

            Logger.Debug($"Version check - Current: {currentVersion}, Saved: {savedVersion ?? "None"}");

            bool isFirstTimeOrMigration = string.IsNullOrEmpty(savedVersion);

            if (isFirstTimeOrMigration || savedVersion != currentVersion)
            {
                string previousVersion = savedVersion ?? "First install / migration";

                globalSettings.modVersionSaved = currentVersion;

                try
                {
                    settingsContainer.Write();
                    Logger.Debug($"Updated saved version from '{previousVersion}' to '{currentVersion}'");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to save settings after version update: {ex.Message}");
                }

                ShowUpdateNotification(currentVersion, previousVersion);
            }
            else
            {
                Logger.Debug("No version change detected");
            }
        }

        public static void ShowUpdateNotification(string newVersion, string oldVersion)
        {
            if (Find.WindowStack == null)
            {
                Logger.Warning("Cannot show update notification - WindowStack is not available yet");
                return;
            }

            try
            {
                OpenVersionHistory(newVersion);
                Logger.Message($"Updated from version {oldVersion} to {newVersion}. Showing new history dialog.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error showing update notification: {ex.Message}");
            }
        }

        private static string FallbackUpdateMessage(string newVersion, string oldVersion)
        {
            return $"RICS has been updated to version {newVersion}.\n\n" +
                   $"Previous version: {(string.IsNullOrEmpty(oldVersion) ? "First install / unknown" : oldVersion)}\n\n" +
                   "Please check the mod's documentation or Steam Workshop page for the detailed changelog.";
        }

        public static void OpenVersionHistory(string highlightVersion = null)
        {
            if (Find.WindowStack == null)
            {
                Logger.Warning("Cannot open version history — WindowStack not ready yet");
                return;
            }

            var dialog = new Dialog_RICS_VersionHistory(highlightVersion);
            Find.WindowStack.Add(dialog);
        }
    }
}
