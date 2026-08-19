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
        private readonly NumericUpDown radius = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 15, DecimalPlaces = 0, Width = 65 };
        private readonly NumericUpDown maximum = new NumericUpDown { Minimum = 1, Maximum = 500, Value = 100, Width = 65 };
        private readonly ComboBox searchPattern = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
        private readonly CheckBox unpopulated = new CheckBox { Text = "Exclude colonised", Checked = true, AutoSize = true };
        private readonly CheckBox noPermit = new CheckBox { Text = "Exclude permit-locked", Checked = true, AutoSize = true };
        private readonly CheckBox useMinimumScore = new CheckBox { Text = "Minimum score", AutoSize = true };
        private readonly NumericUpDown minimumScore = new NumericUpDown { Minimum = -1000, Maximum = 1000, Value = 75, DecimalPlaces = 1, Width = 70, Enabled = false };
        private readonly Button generate = new Button { Text = "Generate route", AutoSize = true };
        private readonly Button push = new Button { Text = "Send to Expedition", AutoSize = true, Enabled = false };
        private readonly Button copyNext = new Button { Text = "Copy next waypoint", AutoSize = true, Enabled = false };
        private readonly CheckBox autoCopyNext = new CheckBox { Text = "Auto-copy after jump", AutoSize = true };
        private readonly Button shortlistCurrent = new Button { Text = "☆ Shortlist current", AutoSize = true, Enabled = false };
        private readonly Label status = new Label { AutoSize = true, Text = "Waiting for current system…" };
        private readonly Label nextWaypoint = new Label { AutoSize = true, Text = "Next waypoint: generate a route" };
        private readonly DataGridView grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, AllowUserToAddRows = false };
        private readonly DataGridView shortlistGrid = new DataGridView {
            Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true
        };
        private readonly Button removeShortlisted = new Button { Text = "Remove selected", AutoSize = true, Enabled = false };
        private readonly Button pushShortlist = new Button { Text = "Send shortlist to Expedition", AutoSize = true, Enabled = false };
        private readonly RouteMapControl routeMap = new RouteMapControl { Dock = DockStyle.Fill };
        private readonly NumericUpDown uncolonisedWeight = Weight(60);
        private readonly NumericUpDown colonisedWeight = Weight(5);
        private readonly NumericUpDown noPermitWeight = Weight(20);
        private readonly NumericUpDown permitWeight = Weight(-100);
        private readonly NumericUpDown scoopableWeight = Weight(15);
        private readonly NumericUpDown distanceWeight = Weight(5);
        private readonly NumericUpDown suitabilityWeight = Weight(25);
        private readonly NumericUpDown resourcesWeight = Weight(15);
        private readonly NumericUpDown arrivalWeight = Weight(10);
        private readonly NumericUpDown hazardWeight = Weight(-25);
        private readonly NumericUpDown confidenceWeight = Weight(5);
        private readonly NumericUpDown unknownDataPercent = new NumericUpDown {
            Minimum = 0, Maximum = 100, Value = 50, DecimalPlaces = 0, Increment = 5, Width = 90
        };
        private readonly Label scoreFormula = new Label { AutoSize = true, MaximumSize = new Size(700, 0) };
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
        private int completedRouteIndex;
        private readonly HashSet<string> shortlistedSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private decimal configuredUncolonisedWeight = 60;
        private decimal configuredColonisedWeight = 5;
        private bool updatingColonisationWeights;

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
                unpopulated, noPermit, useMinimumScore, minimumScore, generate, push,
                copyNext, autoCopyNext, shortlistCurrent
            });
            useMinimumScore.CheckedChanged += (_, __) => minimumScore.Enabled = useMinimumScore.Checked;
            unpopulated.CheckedChanged += (_, __) => UpdateColonisationWeightState();
            var details = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
            details.Controls.Add(ship);
            details.Controls.Add(new Label { Text = "   " });
            details.Controls.Add(status);
            details.Controls.Add(new Label { Text = "   " });
            details.Controls.Add(nextWaypoint);

            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "#", Width = 45 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "State", Width = 75 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "System", Width = 230 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Leg (ly)", Width = 75 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "From centre", Width = 90 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Score", Width = 65 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Primary", Width = 90 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Score breakdown", Width = 420 });
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
            var scoringPage = BuildScoringPage();
            var shortlistPage = BuildShortlistPage();
            tabs.TabPages.Add(routePage);
            tabs.TabPages.Add(mapPage);
            tabs.TabPages.Add(shortlistPage);
            tabs.TabPages.Add(scoringPage);
            Controls.Add(tabs);
            Controls.Add(details);
            Controls.Add(inputs);
            generate.Click += async (_, __) => await GenerateAsync();
            push.Click += (_, __) => PushRoute();
            copyNext.Click += (_, __) => CopyNextWaypoint(true);
            autoCopyNext.CheckedChanged += (_, __) => panelCallbacks?.SaveInt?.Invoke("auto_copy_next", autoCopyNext.Checked ? 1 : 0);
            shortlistCurrent.Click += (_, __) => ToggleCurrentShortlist();
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
            radius.Value = Clamp(callbacks.GetDouble?.Invoke("radius", 15) ?? 15, radius.Minimum, radius.Maximum);
            maximum.Value = Clamp(callbacks.GetInt?.Invoke("maximum", 100) ?? 100, maximum.Minimum, maximum.Maximum);
            searchPattern.SelectedIndex = Math.Max(0, Math.Min(searchPattern.Items.Count - 1, callbacks.GetInt?.Invoke("pattern", 0) ?? 0));
            useMinimumScore.Checked = (callbacks.GetInt?.Invoke("minimum_score_enabled", 0) ?? 0) != 0;
            minimumScore.Value = Clamp(callbacks.GetDouble?.Invoke("minimum_score", 75) ?? 75, minimumScore.Minimum, minimumScore.Maximum);
            autoCopyNext.Checked = (callbacks.GetInt?.Invoke("auto_copy_next", 0) ?? 0) != 0;
            LoadScoreWeights(callbacks);
            LoadShortlist(callbacks);
            UpdateColonisationWeightState();
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
            shortlistCurrent.Enabled = true;
            UpdateShortlistCurrentButton();
            if (entry.name != null && entry.name.Equals("FSDJump", StringComparison.OrdinalIgnoreCase))
                AdvanceRouteProgress(entry.systemname);
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
                    Pattern = (SearchPattern)searchPattern.SelectedIndex, Weights = GetScoreWeights(),
                    MinimumScore = useMinimumScore.Checked ? (double?)minimumScore.Value : null
                };
                var systems = await edsm.GetSphereAsync(origin.Name, settings.RadiusLy, cancellation.Token);
                var preliminarySettings = new SearchSettings {
                    RadiusLy = settings.RadiusLy, MaximumSystems = settings.MaximumSystems,
                    ExcludeColonised = settings.ExcludeColonised, ExcludePermitLocked = settings.ExcludePermitLocked,
                    Pattern = settings.Pattern, Weights = settings.Weights
                };
                var preliminary = scorer.Rank(systems, preliminarySettings);
                status.Text = "Loading body details for " + preliminary.Count + " candidates from EDSM…";
                await edsm.EnrichBodiesAsync(preliminary, cancellation.Token);
                var ranked = scorer.Rank(preliminary, settings);
                route = planner.Plan(origin, ranked, currentShip.JumpRange, settings.Pattern);
                completedRouteIndex = 0;
                RenderRoute();
                routeMap.SetRoute(route);
                panelCallbacks.SaveDouble?.Invoke("radius", settings.RadiusLy);
                panelCallbacks.SaveInt?.Invoke("maximum", settings.MaximumSystems);
                panelCallbacks.SaveInt?.Invoke("pattern", searchPattern.SelectedIndex);
                panelCallbacks.SaveInt?.Invoke("minimum_score_enabled", useMinimumScore.Checked ? 1 : 0);
                panelCallbacks.SaveDouble?.Invoke("minimum_score", (double)minimumScore.Value);
                panelCallbacks.SaveInt?.Invoke("auto_copy_next", autoCopyNext.Checked ? 1 : 0);
                SaveScoreWeights();
                var skipped = ranked.Count - (route.Count - 1);
                status.Text = (route.Count - 1) + " candidates, " + RoutePlanner.TotalDistance(route).ToString("0.0") + " ly route"
                            + (skipped > 0 ? " — " + skipped + " unreachable" : "");
                push.Enabled = route.Count > 1 && panelCallbacks.PushStars != null;
                UpdateRouteProgressDisplay();
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
                var state = i <= completedRouteIndex ? "Visited" : i == completedRouteIndex + 1 ? "Next" : "Pending";
                var rowIndex = grid.Rows.Add(i, state, system.Name,
                    route[i - 1].Coordinates.DistanceTo(system.Coordinates).ToString("0.00"),
                    system.DistanceFromCentre.ToString("0.00"), system.CandidateScore.ToString("0.0"), system.PrimaryStarType ?? "?", system.ScoreBreakdown);
                if (i <= completedRouteIndex)
                    grid.Rows[rowIndex].DefaultCellStyle.ForeColor = Color.Gray;
            }
        }

        private void AdvanceRouteProgress(string systemName)
        {
            if (route.Count <= 1 || string.IsNullOrWhiteSpace(systemName)) return;

            var reachedIndex = completedRouteIndex + 1;
            if (!route[reachedIndex].Name.Equals(systemName, StringComparison.OrdinalIgnoreCase)) return;

            completedRouteIndex = reachedIndex;
            RenderRoute();
            UpdateRouteProgressDisplay();
            status.Text = completedRouteIndex >= route.Count - 1
                ? "Survey route complete."
                : "Waypoint reached: " + systemName + ".";

            // PublishLocation and NewFilteredJournal can both deliver the same jump. Progress
            // only advances once, so automatic copying is naturally de-duplicated.
            if (autoCopyNext.Checked && completedRouteIndex < route.Count - 1)
                CopyNextWaypoint(false);
        }

        private void UpdateRouteProgressDisplay()
        {
            var hasNext = route.Count > 1 && completedRouteIndex < route.Count - 1;
            copyNext.Enabled = hasNext;
            if (route.Count <= 1)
                nextWaypoint.Text = "Next waypoint: generate a route";
            else if (!hasNext)
                nextWaypoint.Text = "Route complete (" + (route.Count - 1) + "/" + (route.Count - 1) + ")";
            else
                nextWaypoint.Text = "Next waypoint: " + route[completedRouteIndex + 1].Name
                    + " (" + completedRouteIndex + "/" + (route.Count - 1) + ")";
        }

        private void CopyNextWaypoint(bool announce)
        {
            if (route.Count <= 1 || completedRouteIndex >= route.Count - 1) return;
            var name = route[completedRouteIndex + 1].Name;
            try
            {
                Clipboard.SetText(name);
                if (announce) status.Text = name + " copied. Paste it into the Galaxy Map search.";
                else status.Text = "Waypoint reached. Next system copied: " + name + ".";
            }
            catch (Exception ex)
            {
                status.Text = "Could not copy the next waypoint: " + ex.Message;
            }
        }

        private void PushRoute()
        {
            var names = route.Skip(1).Select(x => x.Name).ToList();
            status.Text = panelCallbacks.PushStars("expedition", names)
                ? "Route sent to the Expedition panel." : "EDDiscovery could not accept the route.";
        }

        private TabPage BuildShortlistPage()
        {
            var page = new TabPage("Shortlist");
            shortlistGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "System", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            shortlistGrid.SelectionChanged += (_, __) => removeShortlisted.Enabled = shortlistGrid.SelectedRows.Count > 0;
            removeShortlisted.Click += (_, __) => RemoveSelectedShortlistEntries();
            pushShortlist.Click += (_, __) => PushShortlistedSystems();

            var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
            tools.Controls.Add(removeShortlisted);
            tools.Controls.Add(pushShortlist);
            tools.Controls.Add(new Label {
                Text = "Use ☆ Shortlist current while visiting a system to add or remove it.",
                AutoSize = true, Padding = new Padding(8, 6, 0, 0)
            });
            page.Controls.Add(shortlistGrid);
            page.Controls.Add(tools);
            return page;
        }

        private void ToggleCurrentShortlist()
        {
            if (origin == null || string.IsNullOrWhiteSpace(origin.Name)) return;
            if (!shortlistedSystems.Add(origin.Name))
            {
                shortlistedSystems.Remove(origin.Name);
                status.Text = origin.Name + " removed from the shortlist.";
            }
            else status.Text = origin.Name + " added to the shortlist.";
            SaveShortlist();
            RenderShortlist();
            UpdateShortlistCurrentButton();
        }

        private void LoadShortlist(EDDDLLIF.EDDPanelCallbacks callbacks)
        {
            shortlistedSystems.Clear();
            var json = callbacks.GetString?.Invoke("shortlisted_systems", "[]") ?? "[]";
            try
            {
                foreach (var name in new JavaScriptSerializer().Deserialize<string[]>(json) ?? new string[0])
                    if (!string.IsNullOrWhiteSpace(name)) shortlistedSystems.Add(name.Trim());
            }
            catch { /* Ignore malformed settings and start with an empty shortlist. */ }
            RenderShortlist();
        }

        private void SaveShortlist()
        {
            var json = new JavaScriptSerializer().Serialize(shortlistedSystems.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
            panelCallbacks.SaveString?.Invoke("shortlisted_systems", json);
        }

        private void RenderShortlist()
        {
            shortlistGrid.Rows.Clear();
            foreach (var name in shortlistedSystems.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var index = shortlistGrid.Rows.Add(name);
                shortlistGrid.Rows[index].Tag = name;
            }
            removeShortlisted.Enabled = false;
            pushShortlist.Enabled = shortlistedSystems.Count > 0 && panelCallbacks?.PushStars != null;
        }

        private void RemoveSelectedShortlistEntries()
        {
            var names = shortlistGrid.SelectedRows.Cast<DataGridViewRow>()
                .Select(row => row.Tag as string).Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
            foreach (var name in names) shortlistedSystems.Remove(name);
            if (names.Count > 0)
            {
                SaveShortlist();
                RenderShortlist();
                UpdateShortlistCurrentButton();
                status.Text = names.Count + (names.Count == 1 ? " system" : " systems") + " removed from the shortlist.";
            }
        }

        private void PushShortlistedSystems()
        {
            var names = shortlistedSystems.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            status.Text = panelCallbacks.PushStars("expedition", names)
                ? "Shortlist sent to the Expedition panel." : "EDDiscovery could not accept the shortlist.";
        }

        private void UpdateShortlistCurrentButton()
        {
            if (origin == null) { shortlistCurrent.Text = "☆ Shortlist current"; return; }
            shortlistCurrent.Text = shortlistedSystems.Contains(origin.Name) ? "★ Remove current" : "☆ Shortlist current";
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

        private TabPage BuildScoringPage()
        {
            var page = new TabPage("Scoring") { AutoScroll = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12), ColumnCount = 3 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var heading = new Label {
                AutoSize = true, MaximumSize = new Size(760, 0),
                Text = "Higher scores rank earlier. Eligibility filters are applied before scoring, so excluded colonised or permit-locked systems will not appear regardless of their coefficients. Coefficients may be positive or negative."
            };
            layout.Controls.Add(heading, 0, 0);
            layout.SetColumnSpan(heading, 3);
            AddWeightRow(layout, 1, "Uncolonised system", uncolonisedWeight, "Applied when no population or habitation metadata is reported.");
            AddWeightRow(layout, 2, "Colonised system", colonisedWeight, "Applied when population or habitation metadata is present; normally removed by Exclude colonised.");
            AddWeightRow(layout, 3, "No permit required", noPermitWeight, "Applied to systems that can be entered without a permit.");
            AddWeightRow(layout, 4, "Permit required", permitWeight, "Applied to permit-locked systems; normally removed by Exclude permit-locked.");
            AddWeightRow(layout, 5, "Scoopable primary", scoopableWeight, "Applied to O, B, A, F, G, K and M primary stars.");
            AddWeightRow(layout, 6, "Near-centre distance", distanceWeight, "Full value at the centre, fading linearly to zero at the search radius.");
            AddWeightRow(layout, 7, "Body suitability", suitabilityWeight, "Normalised from habitable, terraformable and landable body counts.");
            AddWeightRow(layout, 8, "Resource potential", resourcesWeight, "Normalised from metal-rich worlds and major or pristine rings.");
            AddWeightRow(layout, 9, "Arrival convenience", arrivalWeight, "Rewards useful bodies close to the arrival star; reaches zero at 10,000 ls.");
            AddWeightRow(layout, 10, "Stellar hazard", hazardWeight, "Applied to neutron stars, white dwarfs and black holes.");
            AddWeightRow(layout, 11, "Data confidence", confidenceWeight, "Scaled by EDSM body-data completeness; missing body data uses the configurable unknown default.");
            AddWeightRow(layout, 12, "Unknown data default (%)", unknownDataPercent, "Percentage of each affected coefficient applied when its source data is unavailable. Known zero values remain zero.");
            var presets = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Margin = new Padding(3, 12, 3, 3) };
            presets.Items.AddRange(new object[] { "Balanced colony", "Resource extraction", "Scientific outpost", "Logistics hub", "Remote expansion" });
            presets.SelectedIndex = 0;
            var applyPreset = new Button { Text = "Apply preset", AutoSize = true, Margin = new Padding(3, 12, 3, 3) };
            applyPreset.Click += (_, __) => ApplyPreset(presets.SelectedIndex);
            layout.Controls.Add(presets, 0, 13);
            layout.Controls.Add(applyPreset, 1, 13);
            var reset = new Button { Text = "Restore defaults", AutoSize = true, Margin = new Padding(3, 12, 3, 3) };
            reset.Click += (_, __) => ResetScoreWeights();
            layout.Controls.Add(reset, 0, 14);
            layout.Controls.Add(scoreFormula, 0, 15);
            layout.SetColumnSpan(scoreFormula, 3);
            foreach (var control in AllWeightControls())
                control.ValueChanged += (_, __) => UpdateScoreFormula();
            UpdateScoreFormula();
            page.Controls.Add(layout);
            return page;
        }

        private static void AddWeightRow(TableLayoutPanel layout, int row, string name, NumericUpDown value, string explanation)
        {
            layout.Controls.Add(new Label { Text = name, AutoSize = true, Margin = new Padding(3, 8, 12, 3) }, 0, row);
            layout.Controls.Add(value, 1, row);
            layout.Controls.Add(new Label { Text = explanation, AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(12, 8, 3, 3) }, 2, row);
        }

        private static NumericUpDown Weight(decimal value) => new NumericUpDown {
            Minimum = -500, Maximum = 500, Value = value, DecimalPlaces = 1, Increment = 1, Width = 90
        };

        private ScoreWeights GetScoreWeights() => new ScoreWeights {
            Uncolonised = (double)uncolonisedWeight.Value,
            Colonised = (double)colonisedWeight.Value,
            NoPermitRequired = (double)noPermitWeight.Value,
            PermitRequired = (double)permitWeight.Value,
            ScoopablePrimary = (double)scoopableWeight.Value,
            NearCentre = (double)distanceWeight.Value
            , BodySuitability = (double)suitabilityWeight.Value
            , ResourcePotential = (double)resourcesWeight.Value
            , ArrivalConvenience = (double)arrivalWeight.Value
            , StellarHazard = (double)hazardWeight.Value
            , DataConfidence = (double)confidenceWeight.Value
            , UnknownDataPercent = (double)unknownDataPercent.Value
        };

        private void LoadScoreWeights(EDDDLLIF.EDDPanelCallbacks callbacks)
        {
            uncolonisedWeight.Value = Clamp(callbacks.GetDouble?.Invoke("score_uncolonised", 60) ?? 60, -500, 500);
            colonisedWeight.Value = Clamp(callbacks.GetDouble?.Invoke("score_colonised", 5) ?? 5, -500, 500);
            noPermitWeight.Value = Clamp(callbacks.GetDouble?.Invoke("score_no_permit", 20) ?? 20, -500, 500);
            permitWeight.Value = Clamp(callbacks.GetDouble?.Invoke("score_permit", -100) ?? -100, -500, 500);
            scoopableWeight.Value = Clamp(callbacks.GetDouble?.Invoke("score_scoopable", 15) ?? 15, -500, 500);
            distanceWeight.Value = Clamp(callbacks.GetDouble?.Invoke("score_distance", 5) ?? 5, -500, 500);
            suitabilityWeight.Value = Clamp(callbacks.GetDouble?.Invoke("score_suitability", 25) ?? 25, -500, 500);
            resourcesWeight.Value = Clamp(callbacks.GetDouble?.Invoke("score_resources", 15) ?? 15, -500, 500);
            arrivalWeight.Value = Clamp(callbacks.GetDouble?.Invoke("score_arrival", 10) ?? 10, -500, 500);
            hazardWeight.Value = Clamp(callbacks.GetDouble?.Invoke("score_hazard", -25) ?? -25, -500, 500);
            confidenceWeight.Value = Clamp(callbacks.GetDouble?.Invoke("score_confidence", 5) ?? 5, -500, 500);
            unknownDataPercent.Value = Clamp(callbacks.GetDouble?.Invoke("score_unknown_percent", 50) ?? 50, 0, 100);
            configuredUncolonisedWeight = uncolonisedWeight.Value;
            configuredColonisedWeight = colonisedWeight.Value;
            UpdateScoreFormula();
        }

        private void SaveScoreWeights()
        {
            var weights = GetScoreWeights();
            panelCallbacks.SaveDouble?.Invoke("score_uncolonised", unpopulated.Checked ? (double)configuredUncolonisedWeight : weights.Uncolonised);
            panelCallbacks.SaveDouble?.Invoke("score_colonised", unpopulated.Checked ? (double)configuredColonisedWeight : weights.Colonised);
            panelCallbacks.SaveDouble?.Invoke("score_no_permit", weights.NoPermitRequired);
            panelCallbacks.SaveDouble?.Invoke("score_permit", weights.PermitRequired);
            panelCallbacks.SaveDouble?.Invoke("score_scoopable", weights.ScoopablePrimary);
            panelCallbacks.SaveDouble?.Invoke("score_distance", weights.NearCentre);
            panelCallbacks.SaveDouble?.Invoke("score_suitability", weights.BodySuitability);
            panelCallbacks.SaveDouble?.Invoke("score_resources", weights.ResourcePotential);
            panelCallbacks.SaveDouble?.Invoke("score_arrival", weights.ArrivalConvenience);
            panelCallbacks.SaveDouble?.Invoke("score_hazard", weights.StellarHazard);
            panelCallbacks.SaveDouble?.Invoke("score_confidence", weights.DataConfidence);
            panelCallbacks.SaveDouble?.Invoke("score_unknown_percent", weights.UnknownDataPercent);
        }

        private void ResetScoreWeights()
        {
            uncolonisedWeight.Value = 60; colonisedWeight.Value = 5;
            noPermitWeight.Value = 20; permitWeight.Value = -100;
            scoopableWeight.Value = 15; distanceWeight.Value = 5;
            suitabilityWeight.Value = 25; resourcesWeight.Value = 15;
            arrivalWeight.Value = 10; hazardWeight.Value = -25; confidenceWeight.Value = 5;
            unknownDataPercent.Value = 50;
            configuredUncolonisedWeight = 60; configuredColonisedWeight = 5;
            UpdateColonisationWeightState();
        }

        private NumericUpDown[] AllWeightControls() => new[] {
            uncolonisedWeight, colonisedWeight, noPermitWeight, permitWeight, scoopableWeight,
            distanceWeight, suitabilityWeight, resourcesWeight, arrivalWeight, hazardWeight, confidenceWeight
            , unknownDataPercent
        };

        private void ApplyPreset(int preset)
        {
            ResetScoreWeights();
            if (preset == 1) { suitabilityWeight.Value = 10; resourcesWeight.Value = 60; arrivalWeight.Value = 15; }
            else if (preset == 2) { suitabilityWeight.Value = 55; resourcesWeight.Value = 10; confidenceWeight.Value = 20; }
            else if (preset == 3) { distanceWeight.Value = 25; arrivalWeight.Value = 45; scoopableWeight.Value = 25; hazardWeight.Value = -50; }
            else if (preset == 4) { distanceWeight.Value = -25; suitabilityWeight.Value = 30; resourcesWeight.Value = 25; }
        }

        private void UpdateScoreFormula()
        {
            scoreFormula.Text = "Score = habitation coefficient + permit coefficient"
                + " + scoopable coefficient (when applicable)"
                + " + distance, suitability, resource, arrival, hazard and data-confidence terms. Detailed factors are normalised to 0–1. Unknown inputs use "
                + unknownDataPercent.Value.ToString("0") + "% of the affected coefficient.\n"
                + "Current uncolonised, no-permit, scoopable centre example: "
                + (uncolonisedWeight.Value + noPermitWeight.Value + scoopableWeight.Value + distanceWeight.Value).ToString("0.0");
        }

        private void UpdateColonisationWeightState()
        {
            if (updatingColonisationWeights) return;
            updatingColonisationWeights = true;
            try
            {
                if (unpopulated.Checked)
                {
                    if (uncolonisedWeight.Enabled)
                    {
                        configuredUncolonisedWeight = uncolonisedWeight.Value;
                        configuredColonisedWeight = colonisedWeight.Value;
                    }
                    uncolonisedWeight.Value = 0;
                    colonisedWeight.Value = 0;
                    uncolonisedWeight.Enabled = false;
                    colonisedWeight.Enabled = false;
                }
                else
                {
                    uncolonisedWeight.Enabled = true;
                    colonisedWeight.Enabled = true;
                    uncolonisedWeight.Value = configuredUncolonisedWeight;
                    colonisedWeight.Value = configuredColonisedWeight;
                }
                UpdateScoreFormula();
            }
            finally { updatingColonisationWeights = false; }
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
