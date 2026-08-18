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
        private readonly CheckBox unpopulated = new CheckBox { Text = "Unpopulated only", Checked = true, AutoSize = true };
        private readonly CheckBox noPermit = new CheckBox { Text = "Exclude permit-locked", Checked = true, AutoSize = true };
        private readonly Button generate = new Button { Text = "Generate route", AutoSize = true };
        private readonly Button push = new Button { Text = "Send to Expedition", AutoSize = true, Enabled = false };
        private readonly Label status = new Label { AutoSize = true, Text = "Waiting for current system…" };
        private readonly DataGridView grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, AllowUserToAddRows = false };
        private readonly EdsmClient edsm = new EdsmClient();
        private readonly CandidateScorer scorer = new CandidateScorer();
        private readonly RoutePlanner planner = new RoutePlanner();
        private EDDDLLIF.EDDPanelCallbacks panelCallbacks;
        private StarSystem origin;
        private ShipProfile currentShip = new ShipProfile();
        private IReadOnlyList<StarSystem> route = new List<StarSystem>();
        private CancellationTokenSource cancellation;

        public SurveyorPanel()
        {
            var inputs = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6), WrapContents = true };
            inputs.Controls.AddRange(new Control[] {
                new Label { Text = "Centre", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, centre,
                new Label { Text = "Radius (ly)", AutoSize = true, Padding = new Padding(8, 6, 0, 0) }, radius,
                new Label { Text = "Max systems", AutoSize = true, Padding = new Padding(8, 6, 0, 0) }, maximum,
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
            Controls.Add(grid);
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
            cancellation?.Cancel();
            cancellation = new CancellationTokenSource();
            generate.Enabled = false;
            push.Enabled = false;
            status.Text = "Loading nearby systems from EDSM…";
            try
            {
                var settings = new SearchSettings {
                    RadiusLy = (double)radius.Value, MaximumSystems = (int)maximum.Value,
                    ExcludePopulated = unpopulated.Checked, ExcludePermitLocked = noPermit.Checked
                };
                var systems = await edsm.GetSphereAsync(origin.Name, settings.RadiusLy, cancellation.Token);
                var ranked = scorer.Rank(systems, settings);
                route = planner.Plan(origin, ranked, currentShip.JumpRange);
                RenderRoute();
                panelCallbacks.SaveDouble?.Invoke("radius", settings.RadiusLy);
                panelCallbacks.SaveInt?.Invoke("maximum", settings.MaximumSystems);
                status.Text = (route.Count - 1) + " candidates, " + RoutePlanner.TotalDistance(route).ToString("0.0") + " ly route";
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

        private static ShipProfile ReadShip(EDDDLLIF.JournalEntry entry)
        {
            var profile = new ShipProfile { Name = entry.shipname, Type = entry.shiptype };
            try
            {
                var raw = SurveyorEDDClass.Callbacks.GetShipLoadout?.Invoke("");
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var value = new JavaScriptSerializer().DeserializeObject(raw);
                    profile.JumpRange = FindNumber(value, "JumpRange") ?? FindNumber(value, "MaxJumpRange") ?? 0;
                }
            }
            catch { /* The host may not have a current loadout yet. */ }
            return profile;
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
