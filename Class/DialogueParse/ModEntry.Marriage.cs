using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework.Content;
using StardewModdingAPI;

namespace VoiceOverFrameworkMod
{
    public partial class ModEntry : Mod
    {
        /// <summary>
        /// Convert Marriage (generic + per-spouse) dialogue into voice entries using the shared DialogueUtil.
        /// </summary>
        private IEnumerable<VoiceEntryTemplate> BuildFromMarriageDialogue(
            string characterName, string languageCode, IGameContentHelper content,
            ref int entryNumber, string ext)
        {
            var outList = new List<VoiceEntryTemplate>();

            var marriage = this.GetMarriageDialogueForCharacter(characterName, languageCode, content);
            if (marriage == null || marriage.Count == 0)
                return outList;

            foreach (var item in marriage)
            {
                string processingKey = item.SourceInfo ?? $"Marriage/{characterName}";
                string raw = item.RawText ?? string.Empty;
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                // Use the shared splitter/sanitizer so rules match all other dialogue types
                var pages = DialogueUtil.SplitAndSanitize(raw);

                foreach (var p in pages)
                {
                    string genderTail = string.IsNullOrEmpty(p.Gender) ? "" : "_" + p.Gender;
                    string file = $"{entryNumber}{genderTail}.{ext}";
                    string path = Path.Combine("assets", languageCode, characterName, file).Replace('\\', '/');

                    // Prefer the TK we computed during load/merge; fall back to a readable source tag
                    string tk = !string.IsNullOrWhiteSpace(item.TranslationKey) ? item.TranslationKey : processingKey;

                    outList.Add(new VoiceEntryTemplate
                    {
                        DialogueFrom = processingKey,
                        DialogueText = p.Actor,          // Actor-facing (portrait tags kept)
                        AudioPath = path,
                        TranslationKey = tk,
                        PageIndex = p.PageIndex,
                        DisplayPattern = p.Display,      // Player-facing (portrait tags stripped)
                        GenderVariant = p.Gender
                    });

                    if (this.Config?.developerModeOn == true)
                        this.Monitor?.Log($"[MARRIAGE] + {processingKey} (tk={tk}) p{p.PageIndex} g={p.Gender ?? "na"} -> {path}", LogLevel.Trace);

                    entryNumber++;
                }
            }

            return outList;
        }

        /// <summary>
        /// Detect whether a dialogue key ends with "_NpcName" where NpcName is a known character name
        /// (vanilla OR modded). Example: "Bad_1_Sophia" -> suffix "Sophia".
        /// </summary>
        private bool TryGetNpcSuffix(string key, HashSet<string> knownNames, out string suffix)
        {
            suffix = null;
            if (string.IsNullOrWhiteSpace(key))
                return false;

            int idx = key.LastIndexOf('_');
            if (idx < 0 || idx == key.Length - 1)
                return false;

            string candidate = key.Substring(idx + 1);
            if (knownNames.Contains(candidate))
            {
                suffix = candidate;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Load marriage dialogue (language-aware) for a specific spouse and merge:
        ///   - Characters/Dialogue/MarriageDialogue(.lang)                [generic]
        ///       * keeps keys with no NPC suffix, and keys that end with _{characterName}
        ///       * skips keys that end with _{OtherNpcName}
        ///   - Characters/Dialogue/MarriageDialogue{characterName}(.lang) [per-spouse]
        ///
        /// TranslationKey format:
        ///   - Generic:    "Characters/Dialogue/MarriageDialogue:{Key}"
        ///   - Per-spouse: "Characters/Dialogue/MarriageDialogue{characterName}:{Key}"
        ///
        /// Returns tuples of (RawText, SourceInfo, TranslationKey).
        /// </summary>
        private List<(string RawText, string SourceInfo, string TranslationKey)>
            GetMarriageDialogueForCharacter(string characterName, string languageCode, IGameContentHelper gameContent)
        {
            var results = new List<(string, string, string)>();

            bool isEnglish = languageCode.Equals("en", StringComparison.OrdinalIgnoreCase);
            string langSuffix = isEnglish ? "" : $".{languageCode}";

            // Build once: includes vanilla + modded NPC names so suffix filtering works for SVE spouses like Sophia.
            var knownNames = new HashSet<string>(GetAllKnownCharacterNames(), StringComparer.OrdinalIgnoreCase);

            // 1) Load generic sheet
            Dictionary<string, string> generic = null;
            try
            {
                try
                {
                    generic = gameContent.Load<Dictionary<string, string>>($"Characters/Dialogue/MarriageDialogue{langSuffix}");
                }
                catch (ContentLoadException)
                {
                    // fallback to base (some installs might not ship localized split files in unpacked content)
                    generic = gameContent.Load<Dictionary<string, string>>("Characters/Dialogue/MarriageDialogue");
                }
            }
            catch (ContentLoadException) { /* ignore if missing */ }
            catch (Exception ex)
            {
                this.Monitor?.Log($"Error reading MarriageDialogue{langSuffix}: {ex.Message}", LogLevel.Trace);
            }

            // 2) Load per-spouse sheet
            Dictionary<string, string> perSpouse = null;
            try
            {
                try
                {
                    perSpouse = gameContent.Load<Dictionary<string, string>>($"Characters/Dialogue/MarriageDialogue{characterName}{langSuffix}");
                }
                catch (ContentLoadException)
                {
                    perSpouse = gameContent.Load<Dictionary<string, string>>($"Characters/Dialogue/MarriageDialogue{characterName}");
                }
            }
            catch (ContentLoadException) { /* ignore if missing */ }
            catch (Exception ex)
            {
                this.Monitor?.Log($"Error reading MarriageDialogue{characterName}{langSuffix}: {ex.Message}", LogLevel.Trace);
            }

            if (this.Config?.developerModeOn == true)
                this.Monitor?.Log($"[Marriage] {characterName}: generic={(generic?.Count ?? 0)} perSpouse={(perSpouse?.Count ?? 0)}", LogLevel.Trace);

            // 3) Merge (per-spouse overrides generic)
            var merged = new Dictionary<string, (string Raw, string SourceInfo, string TK)>(StringComparer.OrdinalIgnoreCase);

            // 3a) Generic: keep keys with no NPC suffix OR suffix matches this character
            if (generic != null && generic.Count > 0)
            {
                foreach (var kvp in generic)
                {
                    string key = kvp.Key?.Trim();
                    string raw = kvp.Value;

                    if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(raw))
                        continue;

                    // If the key ends with _<NpcName>, only keep if it matches this NPC.
                    if (TryGetNpcSuffix(key, knownNames, out var suffixName) &&
                        !suffixName.Equals(characterName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string tk = $"Characters/Dialogue/MarriageDialogue:{key}";
                    string src = $"Marriage/{characterName}/{key}";
                    merged[key] = (raw, src, tk);
                }
            }

            // 3b) Per-spouse overrides/adds
            if (perSpouse != null && perSpouse.Count > 0)
            {
                foreach (var kvp in perSpouse)
                {
                    string key = kvp.Key?.Trim();
                    string raw = kvp.Value;

                    if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(raw))
                        continue;

                    string tk = $"Characters/Dialogue/MarriageDialogue{characterName}:{key}";
                    string src = $"Marriage/{characterName}/{key}";
                    merged[key] = (raw, src, tk);
                }
            }

            // 4) Emit in deterministic order (by key)
            foreach (var kvp in merged.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                results.Add((kvp.Value.Raw, kvp.Value.SourceInfo, kvp.Value.TK));

            if (this.Config?.developerModeOn == true)
                this.Monitor?.Log($"[Marriage] {characterName}: {results.Count} merged lines.", LogLevel.Trace);

            return results;
        }
    }
}
