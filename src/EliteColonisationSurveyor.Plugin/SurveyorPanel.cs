using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using EDDDLLInterfaces;
using EliteColonisationSurveyor.Core;

namespace EliteColonisationSurveyor.Plugin
{
    public sealed class SurveyorPanel : UserControl, EDDDLLIF.IEDDPanelExtension
    {
        private static event Action<EDDDLLIF.JournalEntry> CurrentLocationReceived;
        private static readonly object LocationSync = new object();
        private static EDDDLLIF.JournalEntry? latestLocation;
        private readonly TextBox centre = new TextBox { Width = 190, ReadOnly = true };
        private readonly Label ship = new Label { AutoSize = true, Text = "Ship: waiting for EDDiscovery" };
        private readonly NumericUpDown radius = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 50, DecimalPlaces = 0, Width = 65 };
        private readonly NumericUpDown maximum = new NumericUpDown { Minimum = 1, Maximum = 500, Value = 100, Width = 65 };
        private readonly ComboBox searchPattern = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
        private readonly CheckBox unpopulated = new CheckBox { Text = "Exclude colonised", Checked = true, AutoSize = true };
        private readonly CheckBox noPermit = new CheckBox { Text = "Exclude permit-locked", Checked = true, AutoSize = true };
        private readonly Button generate = new Button { Text = "Generate route", AutoSize = true };
        private readonly Button push = new Button { Text = "Send to Expedition", AutoSize = true, Enabled = false };
        private readonly Label status = new Label { AutoSize = true, Text = "Waiting for current system…" };
        private readonly DataGridView grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, AllowUserToAddRows = false };
        private readonly RouteMapControl routeMap = new RouteMapControl { Dock = DockStyle.Fill };
        private readonly EdsmClient edsm = new EdsmClient();
        private readonly CandidateScorer scorer = new CandidateScorer();
        private readonly RoutePlanner planner = new RoutePlanner();
        private EDDDLLIF.EDDPanelCallbacks panelCallbacks;
        private StarSystem origin;
        private ShipProfile currentShip = new ShipProfile();
        private double cachedJumpRange;
        private ulong cachedShipId = ulong.MaxValue;
        private IReadOnlyList<StarSystem> route = new List<StarSystem>();
        private CancellationTokenSource cancellation;

        public SurveyorPanel()
        {
            searchPattern.Items.AddRange(new object[] { "Balanced", "Shortest route", "Concentric shells", "3D spiral", "Octant sweep", "Score first", "Boundary survey", "Jump safe" });
            searchPattern.SelectedIndex = 0;
            var inputs = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6), WrapContents = true };
            inputs.Controls.AddRange(new Control[] {
                new Label { Text = "Centre", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, centre,
                new Label { Text = "Radius (ly)", AutoSize = true, Padding = new Padding(8, 6, 0, 0) }, radius,
                new Label { Text = "Max systems", AutoSize = true, Padding = new Padding(8, 6, 0, 0) }, maximum,
                new Label { Text = "Pattern", AutoSize = true, Padding = new Padding(8, 6, 0, 0) }, searchPattern,
                unpopulated, noPermit, generate, push
            });
            var details = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
            details.Controls.Add(ship);
            details.Controls.Add(new Label { Text = "   " });
            details.Controls.Add(status);

            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "#", Width = 45 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "System", Width = 230 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Leg (ly)", Width = 75 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "From centre", Width = 90 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Score", Width = 65 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Primary", Width = 90 });
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var routePage = new TabPage("Route list");
            routePage.Controls.Add(grid);
            var mapPage = new TabPage("Route map");
            var projection = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 155 };
            projection.Items.AddRange(new object[] { "Top: X / Z", "Front: X / Y", "Side: Z / Y" });
            projection.SelectedIndex = 0;
            projection.SelectedIndexChanged += (_, __) => routeMap.SetViewPreset(projection.SelectedIndex);
            var resetView = new Button { Text = "Reset view", AutoSize = true };
            resetView.Click += (_, __) => { projection.SelectedIndex = 0; routeMap.ResetView(); };
            var mapTools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
            mapTools.Controls.Add(new Label { Text = "Projection", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
            mapTools.Controls.Add(projection);
            mapTools.Controls.Add(resetView);
            mapTools.Controls.Add(new Label { Text = "Drag to rotate · Wheel to zoom", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
            mapPage.Controls.Add(routeMap);
            mapPage.Controls.Add(mapTools);
            tabs.TabPages.Add(routePage);
            tabs.TabPages.Add(mapPage);
            Controls.Add(tabs);
            Controls.Add(details);
            Controls.Add(inputs);
            generate.Click += async (_, __) => await GenerateAsync();
            push.Click += (_, __) => PushRoute();
        }

        internal static void PublishLocation(EDDDLLIF.JournalEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.systemname)) return;
            lock (LocationSync) latestLocation = entry;
            CurrentLocationReceived?.Invoke(entry);
        }

        public void Initialise(EDDDLLIF.EDDPanelCallbacks callbacks, int displayid, string themeasjson, string configuration)
        {
            panelCallbacks = callbacks;
            radius.Value = Clamp(callbacks.GetDouble?.Invoke("radius", 50) ?? 50, radius.Minimum, radius.Maximum);
            maximum.Value = Clamp(callbacks.GetInt?.Invoke("maximum", 100) ?? 100, maximum.Minimum, maximum.Maximum);
            searchPattern.SelectedIndex = Math.Max(0, Math.Min(searchPattern.Items.Count - 1, callbacks.GetInt?.Invoke("pattern", 0) ?? 0));
            CurrentLocationReceived += OnLocationChanged;

            EDDDLLIF.JournalEntry cached;
            lock (LocationSync) cached = latestLocation ?? default(EDDDLLIF.JournalEntry);
            if (!string.IsNullOrWhiteSpace(cached.systemname))
                OnLocationChanged(cached);
            else
                callbacks.RequestTravelGridPosition?.Invoke();
        }

        private void OnLocationChanged(EDDDLLIF.JournalEntry entry)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => OnLocationChanged(entry))); return; }
            origin = new StarSystem { Name = entry.systemname, Coordinates = new Coordinates { X = entry.x, Y = entry.y, Z = entry.z } };
            centre.Text = entry.systemname;
            currentShip = ReadShip(entry);
            ship.Text = "Ship: " + (currentShip.Name ?? currentShip.Type ?? "unknown")
                      + (currentShip.JumpRange > 0 ? " — " + currentShip.JumpRange.ToString("0.0") + " ly jump" : " — jump range unavailable");
            status.Text = "Ready";
        }

        private async Task GenerateAsync()
        {
            if (origin == null) { status.Text = "No current system received from EDDiscovery yet."; return; }
            if (currentShip.JumpRange <= 0) { status.Text = "Cannot generate a safe route: ship jump range is unavailable."; return; }
            cancellation?.Cancel();
            cancellation = new CancellationTokenSource();
            generate.Enabled = false;
            push.Enabled = false;
            status.Text = "Loading nearby systems from EDSM…";
            try
            {
                var settings = new SearchSettings {
                    RadiusLy = (double)radius.Value, MaximumSystems = (int)maximum.Value,
                    ExcludeColonised = unpopulated.Checked, ExcludePermitLocked = noPermit.Checked,
                    Pattern = (SearchPattern)searchPattern.SelectedIndex
                };
                var systems = await edsm.GetSphereAsync(origin.Name, settings.RadiusLy, cancellation.Token);
                var ranked = scorer.Rank(systems, settings);
                route = planner.Plan(origin, ranked, currentShip.JumpRange, settings.Pattern);
                RenderRoute();
                routeMap.SetRoute(route);
                panelCallbacks.SaveDouble?.Invoke("radius", settings.RadiusLy);
                panelCallbacks.SaveInt?.Invoke("maximum", settings.MaximumSystems);
                panelCallbacks.SaveInt?.Invoke("pattern", searchPattern.SelectedIndex);
                var skipped = ranked.Count - (route.Count - 1);
                status.Text = (route.Count - 1) + " candidates, " + RoutePlanner.TotalDistance(route).ToString("0.0") + " ly route"
                            + (skipped > 0 ? " — " + skipped + " unreachable" : "");
                push.Enabled = route.Count > 1 && panelCallbacks.PushStars != null;
            }
            catch (OperationCanceledException) { status.Text = "Search cancelled."; }
            catch (Exception ex)
            {
                status.Text = "Search failed: " + ex.Message;
                SurveyorEDDClass.Callbacks.WriteToLogHighlight?.Invoke("Colonisation Surveyor: " + ex);
            }
            finally { generate.Enabled = true; }
        }

        private void RenderRoute()
        {
            grid.Rows.Clear();
            for (var i = 1; i < route.Count; i++)
            {
                var system = route[i];
                grid.Rows.Add(i, system.Name,
                    route[i - 1].Coordinates.DistanceTo(system.Coordinates).ToString("0.00"),
                    system.DistanceFromCentre.ToString("0.00"), system.CandidateScore.ToString("0.0"), system.PrimaryStarType ?? "?");
            }
        }

        private void PushRoute()
        {
            var names = route.Skip(1).Select(x => x.Name).ToList();
            status.Text = panelCallbacks.PushStars("expedition", names)
                ? "Route sent to the Expedition panel." : "EDDiscovery could not accept the route.";
        }

        private ShipProfile ReadShip(EDDDLLIF.JournalEntry entry)
        {
            var profile = new ShipProfile { Name = KnownValue(entry.shipname), Type = KnownValue(entry.shiptype) };
            if (entry.shipid != ulong.MaxValue && entry.shipid != cachedShipId)
            {
                cachedShipId = entry.shipid;
                cachedJumpRange = 0;
            }
            try
            {
                cachedJumpRange = ReadJumpRange(entry.json) ?? cachedJumpRange;
                var raw = SurveyorEDDClass.Callbacks.GetShipLoadout?.Invoke("");
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var value = new JavaScriptSerializer().DeserializeObject(raw);
                    profile.Name = KnownValue(FindString(value, "ShipUserName"))
                                ?? KnownValue(FindString(value, "Name"))
                                ?? profile.Name;
                    profile.Type = KnownValue(FindString(value, "ShipType")) ?? profile.Type;
                    cachedJumpRange = FindNumber(value, "FSDCurrentRange")
                                   ?? FindNumber(value, "MaxJumpRange")
                                   ?? FindNumber(value, "FSDMaxRange")
                                   ?? cachedJumpRange;
                }
                if (cachedJumpRange <= 0) cachedJumpRange = FindJumpRangeInHistory(entry.totalrecords);
                profile.JumpRange = cachedJumpRange;
            }
            catch { /* The host may not have a current loadout yet. */ }
            return profile;
        }

        private static double? ReadJumpRange(string jsonText)
        {
            if (string.IsNullOrWhiteSpace(jsonText) || jsonText.IndexOf("MaxJumpRange", StringComparison.OrdinalIgnoreCase) < 0) return null;
            var value = new JavaScriptSerializer().DeserializeObject(jsonText);
            return FindNumber(value, "MaxJumpRange");
        }

        private static double FindJumpRangeInHistory(int totalRecords)
        {
            if (SurveyorEDDClass.Callbacks.RequestHistory == null || totalRecords <= 0) return 0;
            var first = Math.Max(1, totalRecords - 10000);
            for (var index = totalRecords; index >= first; index--)
            {
                EDDDLLIF.JournalEntry historyEntry;
                if (!SurveyorEDDClass.Callbacks.RequestHistory(index, false, out historyEntry)) continue;
                var range = ReadJumpRange(historyEntry.json);
                if (range.HasValue && range.Value > 0) return range.Value;
            }
            return 0;
        }

        private static double? FindNumber(object value, string key)
        {
            var map = value as IDictionary<string, object>;
            if (map != null)
            {
                foreach (var pair in map)
                {
                    if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && double.TryParse(Convert.ToString(pair.Value), out var number)) return number;
                    var nested = FindNumber(pair.Value, key); if (nested.HasValue) return nested;
                }
            }
            var list = value as object[];
            if (list != null) foreach (var item in list) { var nested = FindNumber(item, key); if (nested.HasValue) return nested; }
            return null;
        }

        private static string FindString(object value, string key)
        {
            var map = value as IDictionary<string, object>;
            if (map != null)
            {
                foreach (var pair in map)
                    if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) return Convert.ToString(pair.Value);
                foreach (var pair in map)
                {
                    var nested = FindString(pair.Value, key); if (!string.IsNullOrWhiteSpace(nested)) return nested;
                }
            }
            var list = value as object[];
            if (list != null) foreach (var item in list) { var nested = FindString(item, key); if (!string.IsNullOrWhiteSpace(nested)) return nested; }
            return null;
        }

        private static string KnownValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) || value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? null : value;
        }

        private static decimal Clamp(double value, decimal min, decimal max) => Math.Max(min, Math.Min(max, (decimal)value));
        public void Closing() { cancellation?.Cancel(); CurrentLocationReceived -= OnLocationChanged; }
        public bool SupportTransparency => false;
        public bool DefaultTransparent => false;
        public bool AllowClose() => true;
        public string HelpKeyOrAddress() => "https://github.com/EDDiscovery/EDDiscovery/wiki";
        public void InitialDisplay() { }
        public void LoadLayout() { }
        public void SetTransparency(bool ison, Color curcol) { }
        public void TransparencyModeChanged(bool on) { }
        public void ControlTextVisibleChange(bool on) { }
        void EDDDLLIF.IEDDPanelExtension.CursorChanged(EDDDLLIF.JournalEntry je) => OnLocationChanged(je);
        public void NewFilteredJournal(EDDDLLIF.JournalEntry je) => OnLocationChanged(je);
        public void NewUnfilteredJournal(EDDDLLIF.JournalEntry je) { }
        public void HistoryChange(int count, string commander, bool beta, bool legacy) { }
        public void NewUIEvent(string jsonui) { }
        public void NewTarget(Tuple<string, double, double, double> target) { }
        public void ScreenShotCaptured(string file, Size size) { }
        public void ThemeChanged(string themeasjson) { }
    }
}
