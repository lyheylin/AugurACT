using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Advanced_Combat_Tracker;
using AugurACT.Core.EventTexts;
using AugurACT.Core.Host;

namespace AugurACT.Plugin
{
    // Hands an event text's trigger (EventTrigger, from the page) to ACT. Called on the UI thread.
    internal static class ActTriggers
    {
        // ACT's XML share format on the clipboard, to paste into ACT, a chat channel or a file.
        public static NoticeMessage Copy(EventTrigger? trigger)
        {
            try
            {
                var xml = Check(trigger).ToXml();
                Clipboard.SetText(xml);
                return new NoticeMessage { Text = "Trigger XML copied to the clipboard." };
            }
            catch (ArgumentException ex)
            {
                return new NoticeMessage { Text = ex.Message, Error = true };
            }
            catch (ExternalException ex)
            {
                // Another program has the clipboard open.
                return new NoticeMessage { Text = "Couldn't copy to the clipboard: " + ex.Message, Error = true };
            }
        }

        // Adds it to ACT's custom triggers, as importing its XML would. One with the same regex and category is
        // replaced.
        public static NoticeMessage Import(EventTrigger? trigger)
        {
            try
            {
                var t = Check(trigger);
                var act = new CustomTrigger(t.Regex, t.Sound, t.SoundData ?? "", t.Timer, t.TimerName ?? "", t.ResultsTab)
                {
                    Category = t.Category ?? "",
                    RestrictToCategoryZone = t.RestrictToZone,
                };
                var main = ActGlobals.oFormActMain;
                var replaced = main.CustomTriggers.ContainsKey(act.Key);
                main.AddEditCustomTrigger(act);
                main.RebuildActiveCustomTriggers();
                var where = string.IsNullOrEmpty(t.Category) ? "" : $" under {t.Category}";
                return new NoticeMessage { Text = replaced ? $"Trigger updated in ACT{where}." : $"Trigger added to ACT{where}." };
            }
            catch (ArgumentException ex)
            {
                return new NoticeMessage { Text = ex.Message, Error = true };
            }
            catch (Exception ex)
            {
                ActGlobals.oFormActMain.WriteExceptionLog(ex, "AugurACT: trigger import failed");
                return new NoticeMessage { Text = "ACT couldn't add the trigger: " + ex.Message, Error = true };
            }
        }

        private static EventTrigger Check(EventTrigger? trigger) =>
            (trigger ?? throw new ArgumentException("The page sent no trigger.")).Checked();
    }
}
