# PanelCut

Windows desktop panel-cutting optimizer foundation, built with C# / .NET 10 and
WPF using the dotnet CLI and VS Code. This milestone implements **deliverables 1
through 4**: solution scaffold, validated domain models, separate Inventory and
Project JSON persistence, a shared Material catalogue, a read-only guillotine optimizer, a functional WPF
editing/layout interface, and headless xUnit tests.

The executable opens on the Project tab and loads materials and current stock independently.
Material and inventory editing, project dialogs, optimization, and sheet rendering are available.
Drag adjustment, undo/redo, CSV import, and PDF/PNG/CSV export are not implemented
yet. CsvHelper and PDFsharp are intentionally not installed
until their features are implemented.

## Prerequisites

1. On Windows 10/11 x64, install the **.NET 10 SDK (x64)**, not just the runtime,
   from <https://dotnet.microsoft.com/download>. .NET 10 is an LTS release
   supported through November 2028. Restart the terminal after installation.
2. Install **Visual Studio Code** and Microsoft's **C# Dev Kit** extension
   (`ms-dotnettools.csdevkit`). It provides C# editing, debugging, and test
   discovery. Full Visual Studio and its designers are not required.
3. Verify the SDK:

   ```powershell
   dotnet --version
   dotnet --list-sdks
   ```

The version must be 10.x. Initial restore requires access to NuGet. This
milestone was built with SDK 10.0.401 on Windows x64.

## Build, Test, Run

Run these commands from the repository root:

```powershell
dotnet restore PanelCut.slnx
dotnet build PanelCut.slnx --configuration Release
dotnet test tests/PanelCut.Core.Tests/PanelCut.Core.Tests.csproj --configuration Release
dotnet run --project src/PanelCut.App/PanelCut.App.csproj --configuration Release
```

Close the WPF window to end the app. The core and tests target `net10.0`; they can
be built/tested independently without WPF. The complete solution and executable
are intended for Windows; App targets `net10.0-windows` and x64.

## Run and Debug in VS Code

Open the repository root folder in VS Code with C# Dev Kit installed.
In **Run and Debug** (`Ctrl+Shift+D`), select **PanelCut: Desktop**.

- Press `F5` to build in Debug mode and launch with the debugger.
- Set breakpoints in C# files, such as `MainWindow.xaml.cs`, before interacting
  with the app. Press `Shift+F5` to stop debugging.
- Press `Ctrl+F5` to run without debugging, or `Ctrl+Shift+B` to build only.

The launch target is always the desktop app, regardless of the active editor
file. It uses the normal materials and inventory files under
`%AppData%/PanelCut`; debugging does not isolate your saved data.

## Structure

```text
PanelCut.slnx
Directory.Build.props
src/
  PanelCut.Core/
    Models/
    Optimization/
    Persistence/
  PanelCut.App/
tests/
  PanelCut.Core.Tests/
```

- Core owns instance-based models, persistence, and optimization, with no WPF or App references.
- App references Core and owns editable row drafts, native project dialogs,
  manual inventory save triggers, and a DrawingContext-based sheet renderer.
- Core.Tests references Core and uses xUnit. Tests use unique temporary
  directories, never the real AppData inventory.
- Shared compiler settings enable nullable reference types and implicit usings.
- There is no static mutable application state. Pure validation/conversion
  helpers contain no shared mutable data. StockItem shares validation between
  Panel and Scrap; both implement IStockItem.

## Scaffold Commands

These are the CLI commands used to create the foundation. They are for
recreating it in an **empty folder**, not for rerunning over this implementation:

```powershell
dotnet new sln --name PanelCut --format slnx
dotnet new classlib --name PanelCut.Core --output src/PanelCut.Core --framework net10.0
dotnet new wpf --name PanelCut.App --output src/PanelCut.App --framework net10.0
dotnet new xunit --name PanelCut.Core.Tests --output tests/PanelCut.Core.Tests --framework net10.0
dotnet sln PanelCut.slnx add src/PanelCut.Core/PanelCut.Core.csproj src/PanelCut.App/PanelCut.App.csproj tests/PanelCut.Core.Tests/PanelCut.Core.Tests.csproj
dotnet add src/PanelCut.App/PanelCut.App.csproj reference src/PanelCut.Core/PanelCut.Core.csproj
dotnet add tests/PanelCut.Core.Tests/PanelCut.Core.Tests.csproj reference src/PanelCut.Core/PanelCut.Core.csproj
dotnet new gitignore
```

The WPF template uses `--framework net10.0` and generates the Windows target
framework. Shared build settings, x64 targeting, the domain/persistence code,
and the tests were then added manually. The `.slnx` is an MSBuild/CLI solution;
there is no dependency on Visual Studio designer-generated application code.

## Models and Units

**All lengths are stored in millimetres**, including width, height, thickness,
edge trim, blade diameter and kerf. `Project.Unit` selects project input/display units only;
changing it never rescales stored values. Use `UnitConversion.ToMillimetres`
and `FromMillimetres` at application boundaries; one inch is exactly 25.4 mm.

| Model | Data |
| --- | --- |
| Material | Id, Name, Type, Thickness (mm); immutable catalogue entry |
| MaterialCatalogue | Materials list; unique IDs, names unique ignoring case, explicit resolution |
| IStockItem / Panel | Id, Width, Height, MaterialId, Label, Quantity, Priority, CostPerUnit, EdgeTrim; computed UsableWidth, UsableHeight, IsUsable |
| Scrap | Same stock fields, plus nullable OriginPanelId |
| Part | Id, Width, Height, Quantity, Label, MaterialId, Color (#RRGGBB) |
| Inventory | Separate typed Panels and Scraps lists |
| Brand | Id, Name; immutable, names unique ignoring case |
| Blade | Id, Name, Diameter (mm), Teeth, Kerf (mm), optional BrandId, BrandCode; immutable |
| BladeCatalogue | Brands and Blades lists; unique IDs, blade names unique ignoring case, brand references must resolve |
| Project | Parts list, optional BladeId, Unit |

Panels, scraps and parts reference a catalogue material by stable ID. Name, Type
and Thickness are resolved from the current catalogue, never overridden on an
individual item. Successful catalogue edits immediately update every reference,
including projects opened later, without rewriting inventory or project files.
Optimization results remain detached snapshots of their original run.

An origin ID can reference a
panel no longer in inventory; `null` means unknown/manual origin. Removing a
panel does not delete its scraps. Part Color is a `#RRGGBB` string (stored
upper case, default `#D5DDDB` grey) used to fill the part in layouts; all copies
of a part share it. No UI color types are stored in Core.

Validation and defaults:

- Width, height, and thickness must be finite and strictly positive.
- Trim must be finite and nonnegative; EdgeTrim defaults to zero.
- Blade kerf must be finite and nonnegative (**zero kerf is valid**); diameter
  must be positive and teeth at least 1. Blade names cannot be blank; BrandCode
  defaults to empty text. A project's kerf always comes from its selected blade.
- Usable dimensions are width/height minus twice trim. Excessive trim is valid
  metadata; if either usable dimension is nonpositive, IsUsable is false.
- Stock quantity is an integer >= 0; part quantity is an integer >= 1. Both
  default to 1. Depleted stock stays in inventory until manually removed.
- Priority is any signed integer, default 0; lower values are intended to be
  consumed first by the optimizer.
- CostPerUnit is a nonnegative decimal, default 0; currency is not specified
  in this milestone.
- Material Name and Type cannot be blank/null; MaterialId must be nonempty.
  Material thickness must be positive and finite. Labels default to empty text,
  but cannot be null. Material and blade names must be unique, compared
  ignoring case and surrounding spaces; files with duplicates fail to load.
- Project.Unit defaults to Millimetres (other value: Inches).
  Unknown enum values are rejected.
- New records receive nonempty GUIDs. Inventory IDs must be unique across
  panels and scraps; part IDs must be unique within a project.
- Invalid property assignments throw before changing the previous value.
  Call Inventory.Validate()/Project.Validate() after collection changes to
  check null items and duplicate IDs; MaterialCatalogue.Validate() does the same
  for catalogue entries. Stores always validate before saving and
  after loading. Collections are instance-owned mutable Lists; the UI edits
  separate drafts before committing validated models.

## Persistence Contract

Repository defaults:

```text
%AppData%/PanelCut/inventory.json
%AppData%/PanelCut/materials.json
%AppData%/PanelCut/blades.json
```

`InventoryStore`, `MaterialStore` and `BladeStore` use `Environment.SpecialFolder.ApplicationData`
and `Path.Combine`. Pass explicit constructor paths for isolated tests.
`ProjectStore` always takes a caller-selected path, intended to have the
`*.panelcut.json` extension. The File menu uses native Save/Open dialogs.

**Materials, inventory and projects are independent documents.** Project files contain
only parts and job settings, plus schema metadata; never stock or inventory
paths or embedded material definitions. Opening/saving a project does not access
InventoryStore. Share the matching material catalogue alongside projects when
moving them to another installation: matching names alone do not resolve IDs.

- LoadAsync is read-only and creates no files/directories. A missing inventory
  returns a fresh empty Inventory; a missing catalogue returns an empty catalogue.
  A missing requested project is an error.
- Corrupt, incomplete, unknown-field, wrong-document-type, or unsupported-version
  JSON is rejected with InvalidDataException and path context, not replaced
  with an empty document. Filesystem access errors propagate to the caller.
- Loads construct detached models. They never partially update an existing
  caller-owned project or inventory.
- SaveAsync is explicit: there are no startup saves or autosave hooks in setters.
  **Only the Panels and Scraps views' validated manual add/edit/delete actions
  should call InventoryStore.SaveAsync.** No other application feature should
  write Inventory. MaterialStore.SaveAsync is called only for committed manual
  Materials edits. Catalogue edits never write inventory/project files or mark a
  project dirty. BladeStore.SaveAsync is called only for committed manual
  Blades/Brands edits, which likewise never write or dirty the project.
  Optimize has no persistence calls.
- Completing a valid new part automatically saves the current project only if
  it already has a file path. Untitled projects require their first manual save.
  Existing-part edits, deletions and settings changes retain manual saving;
  adding a new part saves the whole current project, including those changes.
  Failed autosaves preserve the file and leave the project dirty for File > Save.
- Saves snapshot and validate the model before filesystem changes, then write
  and flush a unique temporary file beside the destination. Existing files
  are replaced with File.Replace; new files use File.Move. There is no
  delete-first overwrite fallback. Temporary files are cleaned up on failure
  when permissions allow. Local filesystems supporting atomic replacement
  are expected; unsupported replacement errors propagate safely.
- Cancellation before replacement preserves the old file. Replacement is the
  commit point. Access failures and validation failures do not erase old data.
- This milestone assumes one application instance and no concurrent edits
  while taking a save snapshot; multi-process synchronization and backup
  rotation are not implemented.

Example explicit API usage (inventory writes belong to the manual edit
workflow):

```csharp
using PanelCut.Core.Models;
using PanelCut.Core.Persistence;

var inventoryStore = new InventoryStore();
var inventory = await inventoryStore.LoadAsync();
var catalogue = await new MaterialStore().LoadAsync();
var material = catalogue.Materials.First(); // Choose an existing catalogue entry.
var blades = await new BladeStore().LoadAsync();

var project = new Project { BladeId = blades.Blades.First().Id };
project.Parts.Add(new Part(600, 300, material.Id, quantity: 2) { Label = "Shelf" });

var projectStore = new ProjectStore();
await projectStore.SaveAsync(projectPath, project);
var reopened = await projectStore.LoadAsync(projectPath);
```

Here `projectPath` is supplied by the caller. Creating/changing either model
alone does not write any file.

## JSON Schemas

JSON property names and enum strings use camelCase. Persisted records require
Id, Width, Height, and MaterialId. Optional fields
use the defaults above when omitted. Required lists cannot be null; IDs are
preserved, never regenerated to repair invalid files. Derived usable dimensions
and resolved material values are not serialized in stock or parts.

**Inventory files require schema version 2 and project files schema version 3.
Older files are incompatible and are rejected without modification; there is no migration.**
To start fresh, preserve the old files separately and select new repository
paths, or move the old inventory out of its configured path before restarting.
Missing material IDs remain visible for manual reassignment; Optimize fails
explicitly until all input references resolve. Loads never guess a material.

The independent materials document uses schema version 1. Every entry requires
Id, Name, Type and Thickness:

```json
{
  "schemaVersion": 1,
  "materials": [
    {
      "id": "c73849ce-7aef-4c4b-9c12-045c09e88cb9",
      "name": "Oak",
      "type": "Plywood",
      "thickness": 18
    }
  ]
}
```

The independent blades document uses schema version 1 and stores brands and
blades together, so a blade and a brand created with it are saved atomically.
Blade `brandId` and `brandCode` are optional:

```json
{
  "schemaVersion": 1,
  "brands": [
    { "id": "4f0c2d7e-5a36-4c1b-9d2f-8e1a6b3c9d10", "name": "Freud" }
  ],
  "blades": [
    {
      "id": "9b8e7c6d-1a2b-4c3d-8e9f-0a1b2c3d4e5f",
      "name": "Fine crosscut",
      "diameter": 250,
      "teeth": 80,
      "kerf": 3.2,
      "brandId": "4f0c2d7e-5a36-4c1b-9d2f-8e1a6b3c9d10",
      "brandCode": "LU3D 1000"
    }
  ]
}
```

Inventory example:

```json
{
  "schemaVersion": 2,
  "panels": [
    {
      "id": "a3a60045-668e-49cb-9435-d9e7bd2c9722",
      "width": 2440,
      "height": 1220,
      "materialId": "c73849ce-7aef-4c4b-9c12-045c09e88cb9",
      "label": "Oak",
      "quantity": 2,
      "priority": 0,
      "costPerUnit": 125.45,
      "edgeTrim": 10
    }
  ],
  "scraps": []
}
```

Scrap objects have the same stock properties plus `originPanelId` (GUID or null).

Project example:

```json
{
  "schemaVersion": 3,
  "parts": [
    {
      "id": "d26ea2fd-9902-4cc1-85a5-f33e48dc25ea",
      "width": 254,
      "height": 127,
      "materialId": "c73849ce-7aef-4c4b-9c12-045c09e88cb9",
      "quantity": 3,
      "label": "Shelf",
      "color": "#B9D4EB"
    }
  ],
  "bladeId": "9b8e7c6d-1a2b-4c3d-8e9f-0a1b2c3d4e5f",
  "unit": "inches",
  "cutPattern": "byLength"
}
```

Older project files containing `edgeBandTop/Bottom/Left/Right` or `groupTag`
still load; those fields are ignored and not written on the next save.

The example part is 254 x 127 mm, displayed as 10 x 5 inches. A project with
`parts: []` and omitted settings loads with no blade selected, millimetres and
the `optimal` cut pattern.

## Read-Only Optimization

The public entry point is `PanelOptimizer.OptimizePanels(Inventory, Project, MaterialCatalogue, BladeCatalogue)`.
The project's `BladeId` must resolve in the blade catalogue; its kerf is used for
every cut and recorded with the blade name in `result.Settings`:

```csharp
using PanelCut.Core.Optimization;

var result = new PanelOptimizer().OptimizePanels(inventory, project, catalogue, blades);
var sheetsUsed = result.StockItemsUsed;
var jobCost = result.TotalCost;
var wastePercent = result.WastePercentage;
var unplaced = result.UnplacedParts;
```

No inventory quantities, project values, files, or collections are modified.
All working counts are local to the call. Results contain immutable snapshots
and read-only collections, with no references back to mutable input models.
They remain unchanged if the inputs are edited later. Do not edit inputs
concurrently while the optimizer takes its initial snapshot.

### Packing Rules

- Panels and scraps share one priority sequence, ascending. Equal priorities
  retain panel input order followed by scrap input order. Depleted stock and
  stock with a nonpositive usable dimension are skipped.
- Material IDs resolve against the supplied catalogue before packing. Matching
  uses exact, case-sensitive Name and Type plus exact Thickness in canonical mm.
  Different IDs with identical values are compatible; same-name materials with
  different types or thicknesses are not. Missing IDs fail explicitly. The
  optimizer never reads catalogue files or modifies catalogue entries.
- Demand is ordered by descending part area, then descending longest side,
  then original input order. Each physical sheet is filled with any remaining
  matching part that fits, including smaller parts, before opening the next.
  Counts are tracked without eagerly expanding quantities. Empty sheets are
  never included or charged, and remaining identical stock is skipped if no
  part fits an empty unit.
- All parts may rotate by 90 degrees. Both orientations are considered for
  non-square parts on panels and scraps.
- Each sheet is packed many times with different heuristics, and the best
  layout is kept: most part area placed, then the largest remaining offcut,
  then the fewest cuts (`FewestCuts` swaps the last two). Ties keep the first
  heuristic, which reproduces the classic layout described below.
- Heuristics combine a part order (area, longest side, perimeter or shortest
  side, all descending), a fit rule and a split rule. Fit rules: best area
  (minimizes unused rectangle area, then shorter-side remainder), best short
  side, best long side, left-first and top-first (closest free rectangle to
  the sheet origin). Unrotated orientation is preferred on a complete tie.
- Every placement produces up to two guillotine cuts. The split order follows
  the project's `CutPattern`:
  - `Optimal` and `FewestCuts`: all split rules are tried — horizontal-first
    when the remaining vertical distance is at least the horizontal one (the
    classic rule), its reverse, larger or smaller biggest remnant, and fixed
    horizontal or vertical.
  - `ByLength`: always split parallel to the sheet's longer usable side first,
    so the sheet is ripped into full-length strips that are then cross-cut.
  - `ByWidth`: always split parallel to the sheet's shorter usable side first,
    producing strips across the sheet.
  - `StripsByLength` / `StripsByWidth`: same axis as `ByLength` / `ByWidth`,
    but strips are filled in order from the sheet origin with equal-length
    parts grouped, pushing waste into one large offcut.
  Free leaves are never merged, preserving the recorded guillotine cut sequence.
- Kerf is deducted at each separator. No kerf is required outside the usable
  bounds or when a part exactly reaches a free-region edge. A leftover narrower
  than kerf is entirely discarded, with the recorded blade strip clipped to
  that region. Zero kerf allows touching edges. No fit tolerance enlarges bounds.
- Coordinates are mm from the raw sheet's top-left, X right and Y down. The
  initial usable region starts at `(EdgeTrim, EdgeTrim)`. Unit is display
  metadata and never changes geometry.

### Results and Metrics

- `Sheets` includes a stock snapshot, one-based UnitIndex per stock ID,
  Placements, RemainingScraps, and ordered Cuts. Each cut describes its source
  Region, Axis, absolute Position, and effective KerfWidth; replaying these
  splits reconstructs placed and unused leaves.
- Placements include one-based CopyIndex per part ID, Bounds, IsRotated, and
  original part metadata. Edge-banding flags remain in the original part
  orientation; a future renderer/exporter must map them using IsRotated.
- RemainingScraps are geometric rectangles only. They never become Inventory
  Scrap records automatically.
- UnplacedParts gives remaining quantities in original project order.
  IsComplete means every requested copy was placed. Unplaced does not prove
  geometric impossibility: the heuristic may miss another valid arrangement.
- TotalCost charges the full CostPerUnit of each opened physical sheet.
  StockItemsUsed counts physical sheets, not distinct inventory rows.
- WasteArea is opened raw sheet area minus placed part area. It includes trim,
  kerf, and unused offcuts. WastePercentage uses opened raw area as denominator;
  unused stock is excluded. With no sheets opened all metrics are zero.
- Nonrepresentable part areas, opened-stock areas, aggregate areas, or cost
  overflow fail explicitly. A positive kerf too small to advance a remainder
  coordinate at the working numeric scale is rejected rather than silently
  treated as zero.

This is a deterministic heuristic, not a guarantee of minimum sheet count,
minimum cost, or globally optimal yield. Priority takes precedence over cost.
The approach is a clean C# implementation informed by Jukka Jylanki's
[A Thousand Ways to Pack the Bin](https://github.com/juj/RectangleBinPack)
public-domain rectangle-packing research; no native code or external packing
dependency is used.

## Verification and Next Milestone

Tests cover domain defaults/validation, trim including unusable stock, zero and
negative kerf, unit conversion, all-field JSON round trips, optional defaults,
schema separation, missing/corrupt files, duplicate IDs, explicit save behavior,
failed/cancelled saves, and unchanged inventory during project operations.
The locked-destination test exercises Windows sharing/replace semantics and
only executes that check on Windows.

Optimizer tests cover zero/positive kerf, exact fits, both cut orders, stock/part
quantities, mixed panel/scrap priority, material matching, trim, automatic
rotation, unplaced demand, cost/waste, deterministic repeats, and unchanged
Inventory and Project on success and failure. Fixed-seed mixed jobs verify
pairwise spacing, bounds, quantity conservation, area accounting, and replayed
guillotine cuts. Read-only result collections and detached metadata are tested.

Run only optimizer tests with:

```powershell
dotnet test tests/PanelCut.Core.Tests/PanelCut.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~OptimizationTests
```

## Desktop Workflow

The file paths shown in **Panels**, **Scraps** and **Materials** are selectable: use
Ctrl+C or right-click **Copy**. The top **Configuration** menu offers
**Open Inventory Folder** and **Open Materials Folder**, using the configured
locations, including custom paths. A missing folder is reported without creating
files or directories.

1. Open **Materials** and add entries with Name, Type and Thickness (mm).
  Names must be unique (ignoring case); a duplicate name cannot be saved.
  Save a valid row with Enter, by leaving it, or with **Save**.
  Edits save only the catalogue and update every referencing item immediately.
  Invalid/failed saves retain previous effective material values; retry or
  cancel the draft. Permanent deletion is deferred to avoid breaking references
  in unopened projects. Save or cancel pending edits before changing views.
2. Open **Panels** or **Scraps** and add an item. Each view has **All** plus a
  tab per catalogue material name, with independent
  selection. Adding under a material tab prefills that material. Filters never
  remove stock; unresolved references remain visible in All. Select a material by
  name from the
  dropdown and enter dimensions; thickness is read-only from the catalogue.
  All stock lengths are explicitly in mm. Selecting a material fills a blank
  or whitespace-only Label with its Name. Nonblank labels are preserved;
  loading records and renaming catalogue entries never rewrite labels.
  Quantity, priority, cost and trim are editable. Scrap origin is optional. Save a valid row
  with Enter, by leaving the row, or with **Save**. New panels and scraps save
  automatically once the row is valid, even without a saved project. Both views
  share the same inventory file; typing incomplete drafts never writes it.
3. **Cancel edit** or row-level Escape cancels a pending draft. Invalid values
  keep the draft editable and display the error in the status area. If saving
  fails, committed stock/file contents are preserved; **Save** retries and
  Cancel edit restores saved stock. Ctrl-click or Shift-click selects multiple
  rows in Panels, Scraps and Project. **Delete** removes the selected rows;
  stock deletion asks for one confirmation and saves the entire batch atomically.
  Unselected and filtered-out rows are preserved. **Reload stock** reloads the
  independent inventory file. Invalid stock drafts prevent material-filter changes.
4. Open **Blades** and add your saw blades (name, brand, brand code, diameter,
  teeth, kerf; all mm). In the Brand cell pick an existing brand from the
  dropdown, or type a new name and choose **Create "..."** (Enter picks the
  highlighted entry); the new brand is saved together with the blade. Names
  match existing brands ignoring case. The **Brands** list renames brands
  (every blade follows) and deletes only brands no blade uses. Deleting a blade
  used by a project leaves the project showing "Missing blade".
5. Open **Project**, select the blade (its kerf is shown in the project unit),
  add parts, select their materials and set units.
  Part labels remain user-controlled. Width, height, readonly
  thickness and kerf use the selected project unit; stock stays in mm. Dropdown
  option descriptions retain catalogue thickness in mm. Use the
  horizontal scrollbar to reach all edge-banding and group columns in compact
  windows. Blank additions are drafts, not fabricated default stock or parts.
  Completing a valid new part automatically saves an already-saved project.
  Untitled projects still require File > Save. Existing-part edits and deletions
  remain unsaved until File > Save or the next valid part addition.
6. Use **File > Save/Open/New**, or Ctrl+S/Ctrl+O/Ctrl+N. Unsaved project changes
  prompt before replacement or closing. Save As selects another project path;
  selecting any repository path is rejected. Failed opens retain the current
  document. Projects never embed or save inventory.
7. Click **Optimize**. It is refused until the project has an existing blade.
  The UI is disabled while the engine runs off the UI
  thread. Layout shows sheet count, waste, cost, unplaced count and blade/kerf. Use the
  sheet selector or arrows to navigate. Expand Unplaced parts for unmet demand.

Layouts fit the viewport automatically. The outer trim uses a warm hatch,
remaining scrap a pale hatch, and parts their chosen color (Colour column in
the Parts grid; grey by default). Small labels are
clipped or omitted instead of overlapping; hover for full dimensions, label,
copy index and rotation. Dimensions in sheet captions use the result's project
unit; the stock selector retains raw mm dimensions.

Changed committed input invalidates the previous layout. Optimize never changes
stock quantities or writes inventory. Pending stock edits must be saved or
cancelled before other document operations. Invalid/corrupt inventory disables
stock editing and Optimize until it can be successfully reloaded; the app never
silently replaces it with empty stock. Close/file/edit actions are blocked while
a save or optimization is in progress. Invalid/corrupt materials likewise block
material-dependent editing and optimization until **Reload materials** succeeds.
Missing references display a selection-required stock status and can be explicitly
reassigned through normal manual row edits.

### Desktop Verification

The repository keeps three solution projects. A separate .NET 10 file-based
WPF harness exercises the actual DataGrids and dispatcher without adding WPF to
the Core test project:

```powershell
dotnet run --file scripts/VerifyDesktop.cs
```

The harness uses unique temporary materials/inventory/project paths and prints the path
containing its PNG screenshots. It covers stock/part row commits, invalid values,
cancelled additions, locked-file save failures and retry, units, blade
selection and missing blades, scrap origin/trim/quantity validation, deletion, independent
project operations, corrupt inventory recovery, optimization immutability,
sheet navigation and nonblank rendering at 1280x800 and 960x640. It also covers
material creation/editing, real dropdown selections, label autofill/cancel,
live material updates without stock/project writes, catalogue save failure/retry,
corrupt catalogue recovery, unresolved references, old schema rejection and CLI
option validation. Additional checks cover separate stock tabs, material filters
and defaults, material reassignment, duplicate names, missing references, batch
deletion and failed-delete retry, conditional new-part autosave, failed autosave
recovery, and serialized Save/New actions. Blade checks cover the brand
dropdown (create, filter, case-insensitive reuse), cancelled new brands, brand
rename/delete guards, blade validation and corrupt blades recovery. It drives
WPF controls in-process; native Save/Open dialog selection and unsaved-change
confirmation button interactions, stock deletion confirmation, and physical
Ctrl/Shift selection gestures remain manual checks.

For an isolated interactive run, without accessing normal AppData repositories:

```powershell
dotnet run --project src/PanelCut.App --configuration Release -- --inventory-path "$env:TEMP\PanelCut-ManualTest\inventory.json"
```

With `--inventory-path`, the catalogue and blades default to sibling `materials.json`
and `blades.json`. Use `--materials-path <path>` and `--blades-path <path>` to specify
other files; all options are independent and can appear in any order. Repository paths must differ.
Without overrides, the normal AppData paths are used.
No demo stock is seeded. Next is step 5: manual drag reorganization, undo/redo,
CSV import and PDF/PNG/CSV export.