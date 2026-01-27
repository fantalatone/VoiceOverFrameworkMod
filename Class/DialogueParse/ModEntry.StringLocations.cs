using StardewModdingAPI;
using StardewValley;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace VoiceOverFrameworkMod
{
    public partial class ModEntry : Mod
    {
        // ──────────────────────────────────────────────────────────────────────────
        // Strings/Locations loader + shared helpers
        // ──────────────────────────────────────────────────────────────────────────

        private static Dictionary<string, string> LoadStringsLocations(string languageCode, IGameContentHelper content)
        {
            if (content == null) return null;

            string langSuffix = languageCode.Equals("en", StringComparison.OrdinalIgnoreCase) ? "" : $".{languageCode}";
            Dictionary<string, string> dict = null;

            try
            {
                dict = content.Load<Dictionary<string, string>>($"Strings/Locations{langSuffix}");
                if (dict != null && dict.Count > 0)
                    return dict;
            }
            catch { /* fall through */ }

            try
            {
                dict = content.Load<Dictionary<string, string>>("Strings/Locations");
                if (dict != null && dict.Count > 0)
                    return dict;
            }
            catch { /* ignore */ }

            return null;
        }

        private static bool IsCharacterMatch_StringLocations(string speaker, string targetCharacterName) =>
            !string.IsNullOrWhiteSpace(speaker)
            && !string.IsNullOrWhiteSpace(targetCharacterName)
            && speaker.Equals(targetCharacterName, StringComparison.OrdinalIgnoreCase);

        private static bool LooksLikeEventScript(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (!string.IsNullOrWhiteSpace(key) &&
                key.IndexOf("_Event_", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (value.IndexOf("/speak ", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (value.IndexOf("/addTemporaryActor", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (value.IndexOf("/quickQuestion", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (value.IndexOf("/message ", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (value.IndexOf("/end", StringComparison.OrdinalIgnoreCase) >= 0 && value.Contains("/")) return true;

            return false;
        }

        private static string TryGetDialoguePrefixSpeaker(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            int us = key.IndexOf('_');
            if (us <= 0) return null;
            return key.Substring(0, us);
        }

        private static string BuildStringsLocationsTranslationKey(string entryKey, int speakSerial = -1) =>
            (speakSerial >= 0)
                ? $"Strings/Locations:{entryKey}:s{speakSerial}"
                : $"Strings/Locations:{entryKey}";

        // ──────────────────────────────────────────────────────────────────────────
        // 1) Plain dialogue entries (Gourmand_Intro etc)
        // ──────────────────────────────────────────────────────────────────────────
        private IEnumerable<VoiceEntryTemplate> BuildFromStringLocationsDialogue(
            string characterName,
            string languageCode,
            IGameContentHelper content,
            ref int entryNumber,
            string ext
        )
        {
            var outList = new List<VoiceEntryTemplate>();
            if (string.IsNullOrWhiteSpace(characterName) || content == null)
                return outList;

            var dict = LoadStringsLocations(languageCode, content);
            if (dict == null || dict.Count == 0)
                return outList;

            int en = entryNumber;
            bool trace = this.Config?.developerModeOn == true;

            foreach (var kvp in dict)
            {
                string key = kvp.Key;
                string value = kvp.Value;

                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                    continue;

                // Skip anything that looks like an event script here
                if (LooksLikeEventScript(key, value))
                    continue;

                // Only include when key prefix matches character (e.g. "Gourmand_Intro" -> "Gourmand")
                string prefix = TryGetDialoguePrefixSpeaker(key);
                if (!IsCharacterMatch_StringLocations(prefix, characterName))
                    continue;

                var segs = DialogueUtil.SplitAndSanitize(value, splitBAsPage: false);
                if (segs == null || segs.Count == 0)
                    continue;

                foreach (var seg in segs)
                {
                    string genderTail = string.IsNullOrEmpty(seg.Gender) ? "" : $"_{seg.Gender}";
                    string fileName = $"{en}{genderTail}.{ext}";
                    string audioPath = Path.Combine("assets", languageCode, characterName, fileName).Replace('\\', '/');

                    outList.Add(new VoiceEntryTemplate
                    {
                        DialogueFrom = $"Strings/Locations/{key}",
                        DialogueText = seg.Actor,
                        AudioPath = audioPath,
                        TranslationKey = BuildStringsLocationsTranslationKey(key),
                        PageIndex = seg.PageIndex,
                        DisplayPattern = seg.Display,
                        GenderVariant = seg.Gender
                    });

                    if (trace)
                        this.Monitor?.Log($"[STRINGS/LOCATIONS/DLG] + {characterName} <- {key} -> {audioPath}", LogLevel.Info);

                    en++;
                }
            }

            entryNumber = en;
            return outList;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 2) Event/script-like entries (IslandSecret_Event_* etc)
        // ──────────────────────────────────────────────────────────────────────────
        private IEnumerable<VoiceEntryTemplate> BuildFromStringLocationsEvents(
            string characterName,
            string languageCode,
            IGameContentHelper content,
            ref int entryNumber,
            string ext
        )
        {
            var outList = new List<VoiceEntryTemplate>();
            if (string.IsNullOrWhiteSpace(characterName) || content == null)
                return outList;

            var dict = LoadStringsLocations(languageCode, content);
            if (dict == null || dict.Count == 0)
                return outList;

            int en = entryNumber;
            bool trace = this.Config?.developerModeOn == true;

            // Event-style parsing regexes (match your BuildFromEvents approach)
            var speakCommandRegex = new Regex(@"^\s*speak\s+(\w+)\s+""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var namedQuoteRegex = new Regex(@"\b(?:textAboveHead|drawDialogue|message|showText)\s*(\w*)\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var genericQuoteRegex = new Regex(@"""((?:[^""\\]|\\.){4,})""", RegexOptions.Compiled);
            var quickQuestionPrefix = new Regex(@"^\s*quickQuestion\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            foreach (var kvp in dict)
            {
                string key = kvp.Key;
                string value = kvp.Value;

                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                    continue;

                if (!LooksLikeEventScript(key, value))
                    continue;

                string[] commands = value.Split('/');

                string lastSpeaker = null;
                int speakSerialForTarget = -1;

                void EmitFromOneScriptText(string rawText)
                {
                    if (string.IsNullOrWhiteSpace(rawText))
                        return;

                    string unescaped = Regex.Unescape(rawText);

                    // scripts use $b as page breaks (like events)
                    var segs = DialogueUtil.SplitAndSanitize(unescaped, splitBAsPage: true);
                    if (segs == null || segs.Count == 0)
                        return;

                    foreach (var seg in segs)
                    {
                        speakSerialForTarget++;

                        string genderTail = string.IsNullOrEmpty(seg.Gender) ? "" : $"_{seg.Gender}";
                        string fileName = $"{en}{genderTail}.{ext}";
                        string audioPath = Path.Combine("assets", languageCode, characterName, fileName).Replace('\\', '/');

                        outList.Add(new VoiceEntryTemplate
                        {
                            DialogueFrom = $"Strings/Locations/{key}:s{speakSerialForTarget}",
                            DialogueText = seg.Actor,
                            AudioPath = audioPath,
                            TranslationKey = BuildStringsLocationsTranslationKey(key, speakSerialForTarget),
                            PageIndex = seg.PageIndex,
                            DisplayPattern = seg.Display,
                            GenderVariant = seg.Gender
                        });

                        if (trace)
                            this.Monitor?.Log($"[STRINGS/LOCATIONS/EVT] + {characterName} <- {key}:s{speakSerialForTarget} -> {audioPath}", LogLevel.Info);

                        en++;
                    }
                }

                void ProcessOneCommand(string cmdRaw)
                {
                    string command = (cmdRaw ?? "").Trim();
                    if (command.Length == 0) return;

                    // Expand quickQuestion reply blocks after (break)
                    if (quickQuestionPrefix.IsMatch(command) && command.IndexOf("(break)", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var pieces = command.Split(new[] { "(break)" }, StringSplitOptions.None);
                        for (int i = 1; i < pieces.Length; i++)
                        {
                            string normalized = (pieces[i] ?? "").Replace('\\', '/');
                            foreach (var sub in normalized.Split('/'))
                                ProcessOneCommand(sub);
                        }
                        return;
                    }

                    // speak <NPC> "..."
                    var mSpeak = speakCommandRegex.Match(command);
                    if (mSpeak.Success)
                    {
                        string speaker = mSpeak.Groups[1].Value;
                        string captured = mSpeak.Groups[2].Value;
                        lastSpeaker = speaker;

                        if (IsCharacterMatch_StringLocations(speaker, characterName))
                            EmitFromOneScriptText(captured);

                        return;
                    }

                    // drawDialogue/showText/textAboveHead/message <maybeSpeaker> "..."
                    var mNamed = namedQuoteRegex.Match(command);
                    if (mNamed.Success)
                    {
                        string maybeSpeaker = mNamed.Groups[1].Value;
                        string captured = mNamed.Groups[2].Value;

                        if (!string.IsNullOrWhiteSpace(maybeSpeaker))
                            lastSpeaker = maybeSpeaker;

                        if (!string.IsNullOrWhiteSpace(lastSpeaker) && IsCharacterMatch_StringLocations(lastSpeaker, characterName))
                            EmitFromOneScriptText(captured);

                        return;
                    }

                    // generic quotes if context says our NPC is talking
                    if (!string.IsNullOrWhiteSpace(lastSpeaker) && IsCharacterMatch_StringLocations(lastSpeaker, characterName))
                    {
                        foreach (Match gm in genericQuoteRegex.Matches(command))
                        {
                            string captured = gm.Groups[1].Value;
                            string chunk = captured.Trim();
                            if (chunk.Length > 3 && !chunk.StartsWith("..."))
                                EmitFromOneScriptText(captured);
                        }
                    }
                }

                foreach (var raw in commands)
                    ProcessOneCommand(raw);
            }

            entryNumber = en;
            return outList;
        }
    }
}
