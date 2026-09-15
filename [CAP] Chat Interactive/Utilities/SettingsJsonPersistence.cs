// File: SettingsJsonPersistence.cs
//
// Copyright (c) Captolamia
// This file is part of CAP Chat Interactive (RICS).
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE.txt in the project root for full license text.
//
// JSON overlay for ModSettings: prefer-JSON on load, mismatch warning,
// and a sidecar so the checkbox survives XML reset.

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;

namespace CAP_ChatInteractive
{
    /// <summary>
    /// Coordinates JSON settings overlay on top of RimWorld XML ModSettings.
    /// XML is still loaded first; JSON is the recovery / preferred source.
    /// </summary>
    public static class SettingsJsonPersistence
    {
        public const string PolicyFileName = "RICS_SettingsLoadPolicy.json";

        /// <summary>True when XML and Latest JSON disagreed at boot and the dialog has not been shown yet.</summary>
        public static bool HasPendingMismatch { get; private set; }

        /// <summary>
        /// When true, automatic Latest JSON writes must not overwrite a known-good backup
        /// with suspected-bad XML (mismatch Review / dismiss).
        /// </summary>
        public static bool ProtectLatestBackup { get; private set; }

        private static JObject _xmlFingerprintAtMismatch;
        private static int _mismatchDialogRetries;

        /// <summary>Canonical JSON of live settings at the moment a mismatch was detected.</summary>
        private static JObject XmlFingerprintAtMismatch => _xmlFingerprintAtMismatch;

        /// <summary>
        /// Call immediately after GetSettings, before InitializeServices / first-run WriteSettings.
        /// </summary>
        public static void ApplyOnModLoad(CAPChatInteractiveSettings settings)
        {
            HasPendingMismatch = false;
            _xmlFingerprintAtMismatch = null;

            if (settings == null)
                return;

            settings.GlobalSettings ??= new CAPGlobalChatSettings();

            var policy = ReadPolicy();
            if (policy != null)
            {
                settings.GlobalSettings.PreferJsonOnLoad = policy.preferJsonOnLoad;
                ProtectLatestBackup = policy.protectLatestBackup;
            }
            else
            {
                ProtectLatestBackup = false;
            }

            JObject latest;
            try
            {
                latest = JsonFileManager.LoadLatestSettingsBackupJObject();
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to read latest settings JSON backup: {ex.Message}");
                return;
            }

            if (latest == null)
            {
                ProtectLatestBackup = false;
                WritePolicy(settings);
                return;
            }

            if (settings.GlobalSettings.PreferJsonOnLoad)
            {
                if (JsonFileManager.TryApplyLatestBackupInPlace(settings))
                {
                    Logger.Message("Loaded settings from latest JSON backup (prefer JSON on load).");
                    ProtectLatestBackup = false;
                    WritePolicy(settings);
                }
                else
                {
                    Logger.Warning("Prefer JSON on load is enabled but the latest backup could not be applied. Keeping XML settings.");
                }
                return;
            }

            try
            {
                JObject live = JsonFileManager.BuildSettingsSnapshot(settings);
                if (!CanonicalEquals(live, latest))
                {
                    HasPendingMismatch = true;
                    ProtectLatestBackup = true;
                    _xmlFingerprintAtMismatch = Canonicalize(live);
                    WritePolicy(settings);
                    Logger.Warning("XML settings do not match the latest JSON backup. Waiting to warn on the main menu.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to compare XML settings with JSON backup: {ex.Message}");
            }
        }

        /// <summary>Shows the mismatch dialog once WindowStack exists (main menu).</summary>
        public static void ScheduleMismatchDialogIfNeeded()
        {
            if (!HasPendingMismatch)
                return;

            _mismatchDialogRetries = 0;
            LongEventHandler.ExecuteWhenFinished(ShowMismatchDialog);
        }

        /// <summary>Writes the sidecar immediately so a crash before settings-close still honors the checkbox.</summary>
        public static void SetPreferJsonOnLoad(bool enabled)
        {
            var settings = CAPChatInteractiveMod.Instance?.Settings;
            if (settings?.GlobalSettings != null)
                settings.GlobalSettings.PreferJsonOnLoad = enabled;

            WritePolicy(settings);
        }

        /// <summary>
        /// True when close-time auto JSON backup would clobber a good Latest file with
        /// the same suspected-bad XML that triggered the mismatch warning.
        /// Edited settings (fingerprint changed) are allowed through.
        /// </summary>
        public static bool ShouldSkipAutoJsonBackup(CAPChatInteractiveSettings live)
        {
            if (!ProtectLatestBackup)
                return false;

            if (live == null || XmlFingerprintAtMismatch == null)
                return true;

            try
            {
                JObject now = Canonicalize(JsonFileManager.BuildSettingsSnapshot(live));
                return JToken.DeepEquals(now, XmlFingerprintAtMismatch);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not compare live settings to mismatch fingerprint: {ex.Message}");
                return true;
            }
        }

        /// <summary>User explicitly saved or loaded JSON — Latest is trusted again.</summary>
        public static void OnExplicitJsonSave()
        {
            ProtectLatestBackup = false;
            HasPendingMismatch = false;
            _xmlFingerprintAtMismatch = null;
            WritePolicy(CAPChatInteractiveMod.Instance?.Settings);
        }

        /// <summary>
        /// Structural equality after stripping volatile fields and normalizing types.
        /// Safe to call with null (null equals null only).
        /// </summary>
        public static bool CanonicalEquals(JToken left, JToken right)
        {
            if (left == null && right == null)
                return true;
            if (left == null || right == null)
                return false;

            return JToken.DeepEquals(Canonicalize(left), Canonicalize(right));
        }

        public static JObject Canonicalize(JToken token)
        {
            if (token == null)
                return new JObject();

            JToken clone = token.DeepClone();
            StripVolatile(clone);
            return Normalize(clone) as JObject ?? new JObject();
        }

        private static void ShowMismatchDialog()
        {
            try
            {
                if (Find.WindowStack == null)
                {
                    if (_mismatchDialogRetries++ < 10)
                    {
                        LongEventHandler.ExecuteWhenFinished(ShowMismatchDialog);
                        return;
                    }

                    Logger.Warning("Could not show settings mismatch dialog (WindowStack never became available).");
                    return;
                }

                HasPendingMismatch = false;

                var dialog = new Dialog_MessageBox(
                    "RICS.Settings.MismatchBody".Translate(),
                    buttonAText: "RICS.Settings.MismatchLoad".Translate(),
                    buttonAAction: OnLoadFromBackupClicked,
                    buttonBText: "RICS.Settings.MismatchReview".Translate(),
                    buttonBAction: OnReviewClicked,
                    title: "RICS.Settings.MismatchTitle".Translate(),
                    cancelAction: OnMismatchDismissed);

                Find.WindowStack.Add(dialog);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to show settings mismatch dialog: {ex.Message}");
            }
        }

        private static void OnLoadFromBackupClicked()
        {
            var settings = CAPChatInteractiveMod.Instance?.Settings;
            if (settings == null)
            {
                Logger.Error("Cannot load JSON settings backup — mod settings are null.");
                return;
            }

            if (!JsonFileManager.TryApplyLatestBackupInPlace(settings))
            {
                Messages.Message("RICS.Settings.NoBackupFound".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            OnExplicitJsonSave();
            try
            {
                CAPChatInteractiveMod.Instance.WriteSettings();
            }
            catch (Exception ex)
            {
                Logger.Warning($"Applied JSON backup but XML WriteSettings failed: {ex.Message}");
            }

            Messages.Message("RICS.Settings.LoadedFromJson".Translate(), MessageTypeDefOf.NeutralEvent);
        }

        private static void OnReviewClicked()
        {
            ProtectLatestBackup = true;
            WritePolicy(CAPChatInteractiveMod.Instance?.Settings);

            if (Find.WindowStack.WindowOfType<Dialog_ChatInteractiveSettings>() == null)
                Find.WindowStack.Add(new Dialog_ChatInteractiveSettings());
        }

        private static void OnMismatchDismissed()
        {
            ProtectLatestBackup = true;
            WritePolicy(CAPChatInteractiveMod.Instance?.Settings);
        }

        private static SettingsLoadPolicy ReadPolicy()
        {
            try
            {
                string path = JsonFileManager.GetFilePath(PolicyFileName);
                if (!System.IO.File.Exists(path))
                    return null;

                string json = System.IO.File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return null;

                return JsonConvert.DeserializeObject<SettingsLoadPolicy>(json);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not read {PolicyFileName}: {ex.Message}");
                return null;
            }
        }

        private static void WritePolicy(CAPChatInteractiveSettings settings)
        {
            try
            {
                var dto = new SettingsLoadPolicy
                {
                    preferJsonOnLoad = settings?.GlobalSettings?.PreferJsonOnLoad ?? false,
                    protectLatestBackup = ProtectLatestBackup
                };

                string path = JsonFileManager.GetFilePath(PolicyFileName);
                System.IO.File.WriteAllText(path, JsonConvert.SerializeObject(dto, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not write {PolicyFileName}: {ex.Message}");
            }
        }

        private static readonly HashSet<string> VolatileServiceFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "IsConnected"
        };

        private static readonly HashSet<string> VolatileGlobalFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "IsConnected",
            "LiveChatWindowX",
            "LiveChatWindowY",
            "LiveChatWindowWidth",
            "LiveChatWindowHeight",
            "EventsTriggeredThisPeriod",
            "LastEventTick",
            "CooldownPeriodStartTick",
            "PreferJsonOnLoad",
            "modVersion"
        };

        private static void StripVolatile(JToken token)
        {
            if (!(token is JObject root))
                return;

            StripNamed(root["TwitchSettings"] as JObject, VolatileServiceFields);
            StripNamed(root["YouTubeSettings"] as JObject, VolatileServiceFields);
            StripNamed(root["KickSettings"] as JObject, VolatileServiceFields);
            StripNamed(root["GlobalSettings"] as JObject, VolatileGlobalFields);
        }

        private static void StripNamed(JObject obj, HashSet<string> names)
        {
            if (obj == null) return;

            foreach (string name in names)
                obj.Remove(name);
        }

        private static JToken Normalize(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return new JValue("");

            switch (token.Type)
            {
                case JTokenType.Object:
                    var obj = new JObject();
                    foreach (var prop in ((JObject)token).Properties())
                        obj[prop.Name] = Normalize(prop.Value);
                    return obj;

                case JTokenType.Array:
                    var arr = new JArray();
                    foreach (var child in (JArray)token)
                        arr.Add(Normalize(child));
                    return arr;

                case JTokenType.Integer:
                    return new JValue(token.Value<long>());

                case JTokenType.Float:
                    return new JValue(token.Value<double>());

                case JTokenType.String:
                    return new JValue(token.Value<string>() ?? "");

                default:
                    return token.DeepClone();
            }
        }

        /// <summary>Tiny sidecar DTO. Field names match the JSON file (not XML).</summary>
        private sealed class SettingsLoadPolicy
        {
            public bool preferJsonOnLoad;
            public bool protectLatestBackup;
        }
    }
}
