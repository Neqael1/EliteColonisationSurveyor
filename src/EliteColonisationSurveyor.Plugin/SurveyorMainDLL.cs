using System;
using System.Diagnostics;
using EDDDLLInterfaces;

namespace EliteColonisationSurveyor.Plugin
{
    public sealed class SurveyorMainDLL
    {
        internal static EDDDLLIF.EDDCallBacks Callbacks;

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
                null);
            callbacks.WriteToLog?.Invoke("Colonisation Surveyor extension loaded");
            return "0.1.0";
        }

        public void EDDRefresh(string commander, EDDDLLIF.JournalEntry latest) => SurveyorPanel.PublishLocation(latest);
        public void EDDNewJournalEntry(EDDDLLIF.JournalEntry entry) => SurveyorPanel.PublishLocation(entry);
        public void EDDTerminate() => Debug.WriteLine("Colonisation Surveyor unloaded");
    }
}
