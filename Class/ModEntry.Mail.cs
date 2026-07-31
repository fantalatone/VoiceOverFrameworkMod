using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VoiceOverFrameworkMod
{
    public partial class ModEntry : Mod
    {
        private string lastLetterTitle;

        private void CheckForMailV3()
        {
            if (Game1.player == null)
            {
                return;
            }

            bool isLetterBoxVisible = Game1.activeClickableMenu is LetterViewerMenu;

            string currentDisplayedTitle = null;

            if (isLetterBoxVisible)
            {
                var letterViewer = Game1.activeClickableMenu as LetterViewerMenu;

                currentDisplayedTitle = letterViewer?.mailTitle;
            } else
            {
                lastLetterTitle = "";
                ResetDialogueState();
            }

            if (string.IsNullOrWhiteSpace(currentDisplayedTitle))
                return;

            if (currentDisplayedTitle != lastLetterTitle)
            {
                lastLetterTitle = currentDisplayedTitle;

                if (MailPack == null)
                {
                    return;
                }

                if (MailPack.Entries.TryGetValue(currentDisplayedTitle, out string audioPath))
                {
                    bool missingAudio = !System.IO.File.Exists(audioPath);

                    if (!missingAudio && Config.EnableMails)
                        PlayVoiceFromFile(audioPath);
                }
            }
        }
    }
}
