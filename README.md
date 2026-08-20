# Elite Colonisation Surveyor

<p align="center">
  <img src="assets/colonisation-surveyor-icon.png" width="160" alt="Elite Colonisation Surveyor icon">
</p>

An EDDiscovery extension panel that builds an efficient survey route around the
commander's current star system. The first MVP:

- receives the current system, coordinates and ship through EDDiscovery;
- can override the route source with any EDSM system that has known coordinates;
- supports spherical search fields and directed cones with a configurable full
  opening angle and distance;
- reads the current ship loadout and detects its jump range where available;
- fetches known systems inside a user-selected radius from EDSM;
- removes systems with population or habitation metadata before ranking candidates;
- ranks likely colonisation survey candidates with explicit, editable weights;
- orders the candidates with nearest-neighbour routing and a 2-opt improvement;
- visualises the route in a rotatable, zoomable 3D galactic map with view presets;
- offers balanced, shortest-route, shell, spiral, octant, score-first, boundary
  and jump-safe search patterns;
- includes a Scoring tab that explains the ranking formula and lets each profile
  customise the habitation, permit, scoopability and distance coefficients;
- can enrich shortlisted systems with EDSM body data, or Spansh data
  cross-checked against EDSM, and score habitable,
  terraformable and landable bodies, resource potential, arrival convenience,
  stellar hazards and data confidence;
- provides scoring presets, per-system score breakdowns and an optional minimum
  score cutoff before candidates are admitted to the route;
- supports a configurable percentage fallback for coefficients whose source
  data is unavailable;
- keeps a persistent shortlist of systems favourited while they are being
  visited and can send that shortlist to the Expedition panel;
- tracks progress through the generated route, identifies and copies the next
  waypoint for Galaxy Map search, and can auto-copy it after each successful
  hyperspace jump;
- offers an Exploration mode with selectable EDSM or Spansh data sources;
- when Spansh is selected, cross-checks EDSM and combines the catalogues so a
  system is not considered unexplored merely because one service lacks data;
- can target systems with no body data, incomplete catalogues, body-data
  completeness below a chosen percentage, or no more than a chosen number of
  known bodies, without treating lookup failures as unexplored;
- can insert a configurable number of known-data bridge systems between
  Exploration-mode targets when the ship cannot reach them directly;
- pushes the resulting star list into EDDiscovery's Expedition panel;
- keeps the main toolbar compact with icon actions while detailed search and
  waypoint controls live on a dedicated Options tab.

This is an independent, unofficial project and is not endorsed by Frontier
Developments or the EDDiscovery team.

## Project layout

- `src/EliteColonisationSurveyor.Core` – data models, scoring and route planner.
- `src/EliteColonisationSurveyor.Plugin` – .NET Framework 4.8 WinForms panel.
- `tests/EliteColonisationSurveyor.Core.Tests` – dependency-free test runner.

## Install the prebuilt plugin

### Easy installer

1. Open the [latest release](https://github.com/Neqael1/EliteColonisationSurveyor/releases/latest).
2. Download and run `EliteColonisationSurveyor-<version>-Setup.exe`.
3. Windows may show a SmartScreen warning because the installer is not yet
   code-signed. If you downloaded it from this repository, select **More info**
   and **Run anyway**.
4. Close EDDiscovery when prompted, then select **Install**.
5. Start EDDiscovery, approve the newly detected extension, and add
   **Colonisation Surveyor** from the panel selector.

The installer only writes `EliteColonisationSurveyor.Plugin.dll` and
`EliteColonisationSurveyor.Core.dll` to `%LOCALAPPDATA%\EDDiscovery\DLL` for
the current user. Before installing the new pair, it removes older
`EliteColonisationSurveyor*.dll` files and the obsolete `EDDDLLInterfaces.dll`
included with releases up to 0.5.0. It does not require administrator access.

### Manual ZIP installation

1. Open the [latest release](https://github.com/Neqael1/EliteColonisationSurveyor/releases/latest).
2. Download the `EliteColonisationSurveyor-<version>.zip` file from the
   release's **Assets** section.
3. In File Explorer, right-click the downloaded ZIP, select **Properties**,
   tick **Unblock** near the bottom of the **General** tab, and click
   **Apply**. Do this before extracting the ZIP; otherwise Windows may prevent
   EDDiscovery from loading the extracted DLLs without showing a useful error.
4. Close EDDiscovery.
5. Extract both DLL files from the ZIP into EDDiscovery's extension DLL
   directory. This is normally `%LOCALAPPDATA%\EDDiscovery\DLL`; EDDiscovery's
   add-on settings also show the directory it is using.
6. Start EDDiscovery and approve the newly detected extension when prompted.
7. Open the panel selector and add **Colonisation Surveyor**.

Keep both files together:

- `EliteColonisationSurveyor.Plugin.dll`
- `EliteColonisationSurveyor.Core.dll`

Do not copy an `EDDDLLInterfaces.dll` into the extension directory. EDDiscovery
supplies the matching interface assembly itself. If upgrading from version 0.5.0
or earlier, remove the old extension-directory copy before starting EDDiscovery.

To upgrade, close EDDiscovery and replace the existing files with those from
the newer release. To uninstall, close EDDiscovery and remove both files.

If the plugin was extracted before the ZIP was unblocked, close EDDiscovery,
right-click each installed plugin DLL, select **Properties**, tick **Unblock**,
and click **Apply**. Alternatively, open PowerShell and run:

```powershell
Get-ChildItem "$env:LOCALAPPDATA\EDDiscovery\DLL\EliteColonisationSurveyor*.dll" |
    Unblock-File
```

## Build

On Windows with Visual Studio 2022 or the Build Tools installed:

```powershell
dotnet build .\EliteColonisationSurveyor.sln -c Release
```

Copy these two output files to EDDiscovery's extension DLL directory (shown in
EDDiscovery's extension/add-on settings):

- `EliteColonisationSurveyor.Plugin.dll`
- `EliteColonisationSurveyor.Core.dll`

Restart EDDiscovery, approve the newly detected extension, and add the
**Colonisation Surveyor** panel from the panel selector.

The EDDiscovery interface source is vendored from
`EDDiscovery/EliteDangerousCore` under Apache-2.0; see `THIRD_PARTY_NOTICES.md`.

## Current assumptions

EDSM only returns systems already submitted by commanders, so the search field
is a survey pattern over *known* stars rather than a complete catalogue of every
procedurally generated star. The public sphere API has a maximum radius of 100
ly, which the panel enforces. Spansh is available as an Exploration-mode body
data source; its results are automatically cross-checked with EDSM. Missing
catalogue data is evidence of an exploration opportunity, not proof that a
system has never been visited. A candidate score is guidance, not a guarantee
of in-game colonisation eligibility; eligibility and body details must be
checked in game as systems are visited.

The default score favours unpopulated, non-permit systems and scoopable primary
stars. Distance from the centre is a small tie-breaker. The generated route
always starts at the centre and excludes systems beyond the selected radius.
