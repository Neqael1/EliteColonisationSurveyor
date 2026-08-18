// Derived from EDDiscovery/EliteDangerousCore (Apache-2.0).
using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace EDDDLLInterfaces
{
    public static class EDDDLLIF
    {
        [StructLayout(LayoutKind.Explicit, Size = 424)]
        public struct JournalEntry
        {
            [FieldOffset(0)] public int ver;
            [FieldOffset(4)] public int indexno;
            [FieldOffset(8), MarshalAs(UnmanagedType.BStr)] public string utctime;
            [FieldOffset(16), MarshalAs(UnmanagedType.BStr)] public string name;
            [FieldOffset(56), MarshalAs(UnmanagedType.BStr)] public string systemname;
            [FieldOffset(64)] public double x;
            [FieldOffset(72)] public double y;
            [FieldOffset(80)] public double z;
            [FieldOffset(112), MarshalAs(UnmanagedType.BStr)] public string shiptype;
            [FieldOffset(176), MarshalAs(UnmanagedType.BStr)] public string json;
            [FieldOffset(200), MarshalAs(UnmanagedType.BStr)] public string shipident;
            [FieldOffset(208), MarshalAs(UnmanagedType.BStr)] public string shipname;
            [FieldOffset(368)] public long systemaddress;
        }

        public delegate string EDDShipLoadout([MarshalAs(UnmanagedType.BStr)] string name);
        public delegate void EDDAddPanel(string id, Type paneltype, string wintitle, string refname, string description, Image img);
        public delegate void EDDString([MarshalAs(UnmanagedType.BStr)] string value);

        [StructLayout(LayoutKind.Explicit, Size = 136)]
        public struct EDDCallBacks
        {
            [FieldOffset(0)] public int ver;
            [FieldOffset(24)] public EDDShipLoadout GetShipLoadout;
            [FieldOffset(32)] public EDDAddPanel AddPanel;
            [FieldOffset(48)] public EDDString WriteToLog;
            [FieldOffset(56)] public EDDString WriteToLogHighlight;
        }

        public class EDDPanelCallbacks
        {
            public int ver;
            public delegate void PanelSave<T>(string key, T value);
            public delegate T PanelGet<T>(string key, T defaultValue);
            public delegate bool PanelBool();
            public delegate void PanelString(string value);
            public delegate bool PanelPushStarsList(string panelName, System.Collections.Generic.List<string> stars);
            public PanelSave<string> SaveString;
            public PanelSave<double> SaveDouble;
            public PanelSave<int> SaveInt;
            public PanelGet<string> GetString;
            public PanelGet<double> GetDouble;
            public PanelGet<int> GetInt;
            public PanelString SetControlText;
            public PanelBool IsClosed;
            public PanelBool RequestTravelGridPosition;
            public PanelPushStarsList PushStars;
        }

        public interface IEDDPanelExtension
        {
            void Initialise(EDDPanelCallbacks callbacks, int displayid, string themeasjson, string configuration);
            void SetTransparency(bool ison, Color curcol);
            void LoadLayout();
            void InitialDisplay();
            void CursorChanged(JournalEntry je);
            void Closing();
            bool SupportTransparency { get; }
            bool DefaultTransparent { get; }
            void TransparencyModeChanged(bool on);
            bool AllowClose();
            string HelpKeyOrAddress();
            void ControlTextVisibleChange(bool on);
            void HistoryChange(int count, string commander, bool beta, bool legacy);
            void NewUnfilteredJournal(JournalEntry je);
            void NewFilteredJournal(JournalEntry je);
            void NewUIEvent(string jsonui);
            void NewTarget(Tuple<string, double, double, double> target);
            void ScreenShotCaptured(string file, Size size);
            void ThemeChanged(string themeasjson);
        }
    }
}
