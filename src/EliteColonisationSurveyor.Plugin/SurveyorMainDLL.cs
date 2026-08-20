using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using EDDDLLInterfaces;

namespace EliteColonisationSurveyor.Plugin
{
    public sealed class SurveyorEDDClass
    {
        internal static EDDDLLIF.EDDCallBacks Callbacks;
        private static readonly Image PanelIcon = LoadPanelIcon();

        public string EDDInitialise(string hostVersion, string dllFolder, EDDDLLIF.EDDCallBacks callbacks)
        {
            Callbacks = callbacks;
            if (callbacks.ver < 3 || callbacks.AddPanel == null)
                return "!Colonisation Surveyor requires EDDiscovery callback interface 3 or later";

            callbacks.AddPanel(
                "acameron-colonisation-surveyor",
                typeof(SurveyorPanel),
                "Colonisation Surveyor",
                "ColonisationSurveyor",
                "Optimised colonisation candidate survey routes",
                PanelIcon);
            callbacks.WriteToLog?.Invoke("Colonisation Surveyor extension loaded");
            return "0.11.2";
        }

        public void EDDRefresh(string commander, EDDDLLIF.JournalEntry latest) => SurveyorPanel.PublishLocation(latest);
        public void EDDNewJournalEntry(EDDDLLIF.JournalEntry entry) => SurveyorPanel.PublishLocation(entry);
        public void EDDTerminate() => Debug.WriteLine("Colonisation Surveyor unloaded");

        private static Image LoadPanelIcon()
        {
            const string resource = "EliteColonisationSurveyor.Plugin.Resources.colonisation-surveyor-icon.png";
            using (Stream stream = typeof(SurveyorEDDClass).Assembly.GetManifestResourceStream(resource))
            {
                if (stream == null) return null;
                using (var source = Image.FromStream(stream)) return new Bitmap(source);
            }
        }
    }
}
