#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../src/PanelCut.App/PanelCut.App.csproj

using System.IO;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PanelCut.App;
using PanelCut.App.Presentation;
using PanelCut.App.Controls;
using PanelCut.Core.Models;
using PanelCut.Core.Optimization;
using PanelCut.Core.Persistence;
using Panel = PanelCut.Core.Models.Panel;

internal static class VerifyDesktop
{
    private static readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "PanelCut.Desktop", Guid.NewGuid().ToString("N"));

    [STAThread]
    private static void Main()
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Startup += async (_, _) =>
        {
            MainWindow? window = null;
            try
            {
                var automaticMaterial = new MaterialRow();
                automaticMaterial.Type = "Plywood";
                Check(automaticMaterial.Name == "Plywood", "Type fills an empty material name");
                automaticMaterial.BeginEdit();
                automaticMaterial.Thickness = "18";
                Check(automaticMaterial.Name == "Plywood 18 mm", "Thickness adds its value and unit to a type-only name");
                automaticMaterial.CancelEdit();
                Check(automaticMaterial.Name == "Plywood" && automaticMaterial.Thickness == "", "Cancel restores material name and thickness without autofill");
                automaticMaterial.Name = "Custom name";
                automaticMaterial.Type = "MDF";
                automaticMaterial.Thickness = "12";
                Check(automaticMaterial.Name == "Custom name", "Material defaults preserve custom names");
                var loadedMaterial = new MaterialRow(new Material("Plywood", "Plywood", 18));
                Check(loadedMaterial.Name == "Plywood", "Loading a type-only material name does not change it");
                var parse = typeof(App).GetMethod("ParsePaths", BindingFlags.Static | BindingFlags.NonPublic)!;
                var paths = ((string? Inventory, string? Materials, string? Blades))parse.Invoke(null,
                    [new[] { "--materials-path", "catalogue.json", "--blades-path", "saws.json", "--inventory-path", "stock.json" }])!;
                Check(paths.Inventory == "stock.json" && paths.Materials == "catalogue.json" && paths.Blades == "saws.json",
                    "Repository CLI options parse independently in either order");
                foreach (var invalid in new[] { new[] { "--materials-path" }, new[] { "--bad", "file.json" },
                    new[] { "--materials-path", "a.json", "--materials-path", "b.json" }, new[] { "--blades-path", "a.json", "--blades-path", "b.json" } })
                {
                    try { parse.Invoke(null, [invalid]); throw new Exception("Invalid CLI arguments accepted"); }
                    catch (TargetInvocationException exception) when (exception.InnerException is ArgumentException) { }
                }
                Check(true, "Malformed or duplicate CLI arguments rejected");
                window = new MainWindow(Path.Combine(DirectoryPath, "stock files", "inventory.json"),
                    Path.Combine(DirectoryPath, "materials catalogue", "materials.json"),
                    Path.Combine(DirectoryPath, "blade files", "blades.json"));
                window.Show();
                await Drain();
                var workspace = Field<WorkspaceViewModel>(window, "workspace");
                Check(workspace.InventoryReady, "Missing inventory loads successfully");
                Check(!Directory.Exists(DirectoryPath), "Startup does not create inventory");
                Check(Field<TabControl>(window, "Views").SelectedIndex == 0, "Project is the initial view");
                Check(Field<TabControl>(window, "Views").Items.Cast<TabItem>().Select(tab => tab.Header.ToString())
                    .SequenceEqual(new[] { "Project", "Panels", "Scraps", "Layout", "Materials", "Blades" }), "Stock has separate top-level tabs");

                var configurationMenu = Field<MenuItem>(window, "ConfigurationMenu");
                Check(Field<Menu>(window, "FileMenu").Items.Contains(configurationMenu)
                    && configurationMenu.Items.Count == 3, "Top menu includes all configuration folder commands");
                foreach (var (controlName, menuName, path, tabName) in new[]
                {
                    ("InventoryLocation", "OpenInventoryFolderMenuItem", workspace.InventoryPath, "PanelsTab"),
                    ("ScrapsLocation", "OpenInventoryFolderMenuItem", workspace.InventoryPath, "ScrapsTab"),
                    ("MaterialsLocation", "OpenMaterialsFolderMenuItem", workspace.MaterialsPath, "MaterialsTab"),
                    ("BladesLocation", "OpenBladesFolderMenuItem", workspace.BladesPath, "BladesTab")
                })
                {
                    SelectTab(window, tabName);
                    await Drain();
                    var location = Field<TextBox>(window, controlName);
                    Check(location.IsReadOnly && location.Text == path, $"{controlName} displays the complete configured path read-only");
                    location.Focus();
                    location.SelectAll();
                    Check(location.SelectedText == path && ApplicationCommands.Copy.CanExecute(null, location),
                        $"{controlName} supports full-path selection and Copy");
                    location.Select(0, 0);
                    var menuItem = Field<MenuItem>(window, menuName);
                    Check(configurationMenu.Items.Contains(menuItem), $"{menuName} belongs to Configuration menu");
                    menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Check(Field<TextBox>(window, "Status").Text.Contains(Path.GetDirectoryName(path)!, StringComparison.Ordinal),
                        $"{menuName} reports the correct missing custom folder");
                }
                Check(!Directory.Exists(DirectoryPath) && !workspace.IsDirty,
                    "Folder commands do not create files or folders or dirty the project");
                Field<TabControl>(window, "Views").SelectedIndex = 0;

                Check(workspace.MaterialsReady, "Missing catalogue loads successfully");
                SelectTab(window, "MaterialsTab");
                Call(window, "AddMaterialClick");
                await Drain();
                var materialsGrid = Field<DataGrid>(window, "MaterialsGrid");
                var oakRow = workspace.Materials.Single();
                SetCell(materialsGrid, oakRow, "Name", "Oak");
                SetCell(materialsGrid, oakRow, "Type", "Plywood");
                SetCell(materialsGrid, oakRow, "Thickness", "18");
                Check(materialsGrid.CommitEdit(DataGridEditingUnit.Row, true), "Material row commits");
                await Field<Task>(window, "materialSave");
                var oakId = oakRow.Id;
                Check(File.Exists(workspace.MaterialsPath) && !File.Exists(workspace.InventoryPath), "Material creation saves only catalogue");
                Check(workspace.MaterialTypes.SequenceEqual(new[] { "Plywood" }), "New material type becomes a dropdown option after saving");
                Call(window, "AddMaterialClick");
                await Drain();
                var birchRow = workspace.Materials.Last();
                SetCell(materialsGrid, birchRow, "Name", "Birch");
                SetCell(materialsGrid, birchRow, "Type", "Plywood");
                SetCell(materialsGrid, birchRow, "Thickness", "12");
                Check(materialsGrid.CommitEdit(DataGridEditingUnit.Row, true), "Second material commits");
                await Field<Task>(window, "materialSave");
                var birchId = birchRow.Id;
                Check(workspace.MaterialTypes.Count == 1, "Repeated material types appear only once in the dropdown");
                var materialBytes = await File.ReadAllBytesAsync(workspace.MaterialsPath);
                foreach (var invalidThickness in new[] { "0", "-1", "", "NaN", "Infinity" })
                {
                    birchRow = workspace.Materials.Single(material => material.Id == birchId);
                    SetCell(materialsGrid, birchRow, "Thickness", invalidThickness);
                    Check(!materialsGrid.CommitEdit(DataGridEditingUnit.Row, true), $"Invalid thickness '{invalidThickness}' cannot commit");
                    Call(window, "CancelMaterialClick");
                    await Drain();
                }
                Check(Enumerable.SequenceEqual(materialBytes, await File.ReadAllBytesAsync(workspace.MaterialsPath)), "Cancelled invalid material preserves catalogue");
                Call(window, "AddMaterialClick");
                await Drain();
                var defaultRow = workspace.Materials.Last();
                SetCell(materialsGrid, defaultRow, "Type", "Plywood");
                Check(defaultRow.Name == "Plywood", "Dropdown selection supplies a blank name in the grid");
                SetCell(materialsGrid, defaultRow, "Thickness", "18");
                Check(defaultRow.Name == "Plywood 18 mm", "Grid thickness commit generates the full material name");
                Call(window, "CancelMaterialClick");
                await Drain();
                birchRow = workspace.Materials.Single(material => material.Id == birchId);
                var nameContent = (TextBlock)materialsGrid.Columns[0].GetCellContent(birchRow);
                Check(nameContent.VerticalAlignment == VerticalAlignment.Center && nameContent.TextAlignment == TextAlignment.Left,
                    "Material names are vertically centered and remain left-aligned");
                var thicknessColumn = materialsGrid.Columns.Single(column => column.SortMemberPath == "Thickness");
                var thicknessContent = Find<TextBlock>(thicknessColumn.GetCellContent(birchRow))!;
                Check(thicknessContent.TextAlignment == TextAlignment.Right, "Displayed thickness is right-aligned");
                materialsGrid.CurrentCell = new DataGridCellInfo(birchRow, thicknessColumn);
                materialsGrid.BeginEdit();
                materialsGrid.UpdateLayout();
                var thicknessInput = Find<PositiveNumberInput>(thicknessColumn.GetCellContent(birchRow))!;
                Check(thicknessInput is not null, "Thickness uses a numeric spin editor");
                Check(thicknessInput!.TextAlignment == TextAlignment.Right, "Thickness editor is right-aligned");
                VerifyThicknessInput(thicknessInput!);
                Capture(window, "materials-spinner-1280.png");
                window.Width = 960;
                window.UpdateLayout();
                Capture(window, "materials-spinner-960.png");
                window.Width = 1280;
                Call(window, "CancelMaterialClick");
                await Drain();
                Check(birchRow.Thickness == "12", "Cancelling spinner edits restores thickness");
                Check(Enumerable.SequenceEqual(materialBytes, await File.ReadAllBytesAsync(workspace.MaterialsPath)),
                    "Spinner drafts do not persist on cancel");
                Capture(window, "materials-1280.png");

                Field<TabControl>(window, "Views").SelectedIndex = 1;
                Call(window, "AddPanelClick");
                await Drain();
                var grid = Field<DataGrid>(window, "PanelsGrid");
                foreach (var stockGrid in new[] { grid, Field<DataGrid>(window, "ScrapsGrid") })
                {
                    Check((string)stockGrid.Columns.OrderBy(column => column.DisplayIndex).First().Header == "Label",
                        $"{stockGrid.Name} starts with Label");
                    Check(stockGrid.CanUserReorderColumns && stockGrid.Columns.All(column => column.CanUserReorder),
                        $"{stockGrid.Name} allows column reordering");
                    stockGrid.Columns[0].DisplayIndex = 2;
                    Check(stockGrid.Columns[0].DisplayIndex == 2, $"{stockGrid.Name} accepts a changed column order");
                    stockGrid.Columns[0].DisplayIndex = 0;
                }
                var row = workspace.Panels.Single();
                SelectMaterial(grid, row, oakId);
                Check(row.Label == "Oak" && row.Thickness == "18", "Panel selection fills blank label and derived thickness");
                SetCell(grid, row, "Width", "1200");
                SetCell(grid, row, "Height", "800");
                SetCell(grid, row, "Quantity", "3");
                SetCell(grid, row, "Priority", "-2");
                SetCell(grid, row, "EdgeTrim", "10");
                Check(grid.CommitEdit(DataGridEditingUnit.Cell, true), "Valid stock cell commits");
                Check(grid.CommitEdit(DataGridEditingUnit.Row, true), "Valid stock row commits");
                await Field<Task>(window, "stockSave");
                await Drain();
                Check(File.Exists(workspace.InventoryPath), "Manual stock commit creates file");
                var stock = await new InventoryStore(workspace.InventoryPath).LoadAsync();
                Check(stock.Panels.Single().Priority == -2 && stock.Panels[0].EdgeTrim == 10, "Stock metadata persists");
                var before = await File.ReadAllBytesAsync(workspace.InventoryPath);

                SetCell(grid, row, "Width", "-1");
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                Check(!grid.CommitEdit(DataGridEditingUnit.Row, true), "Invalid stock row is rejected");
                Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(workspace.InventoryPath)), "Invalid stock edit leaves disk unchanged");
                Call(window, "CancelStockClick");
                await Drain();
                Check(workspace.Panels[0].Width == "1200", "Cancelled stock draft restores saved value");

                row = workspace.Panels[0];
                using (var locked = new FileStream(workspace.InventoryPath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    SetCell(grid, row, "Quantity", "4");
                    grid.CommitEdit(DataGridEditingUnit.Row, true);
                    await Field<Task>(window, "stockSave");
                    Check(workspace.Inventory.Panels[0].Quantity == 3, "Failed save preserves committed inventory");
                    Check(Field<bool>(window, "stockPending"), "Failed save remains pending");
                }
                Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(workspace.InventoryPath)), "Failed save preserves file bytes");
                Call(window, "RetryStockClick");
                await Drain();
                await Field<Task>(window, "stockSave");
                Check(workspace.Inventory.Panels[0].Quantity == 4, "Failed save can be retried");
                before = await File.ReadAllBytesAsync(workspace.InventoryPath);
                Call(window, "AddPanelClick");
                await Drain();
                grid.CancelEdit(DataGridEditingUnit.Cell);
                grid.CancelEdit(DataGridEditingUnit.Row);
                await Drain();
                Check(workspace.Panels.Count == 1 && !Field<bool>(window, "stockPending"), "Escape cancels an added stock row");
                Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(workspace.InventoryPath)), "Cancelled add leaves file unchanged");

                var (fineId, ripId) = await VerifyBladesAsync(window, workspace);
                Field<TabControl>(window, "Views").SelectedIndex = 0;
                Call(window, "AddPartClick");
                var parts = Field<DataGrid>(window, "PartsGrid");
                var part = workspace.Parts.Single();
                SetCell(parts, part, "Label", "Shelf");
                SetCell(parts, part, "Width", "254");
                SetCell(parts, part, "Height", "127");
                SelectMaterial(parts, part, oakId);
                Check(part.Label == "Shelf" && part.Thickness == "18", "Part selection supplies thickness and preserves label");
                SetCell(parts, part, "Quantity", "6");
                parts.CommitEdit(DataGridEditingUnit.Cell, true);
                Check(parts.CommitEdit(DataGridEditingUnit.Row, true), "Valid part row commits");
                Check(workspace.Project.Parts.Single().Width == 254, "Part model receives committed width");
                Field<ComboBox>(window, "UnitInput").SelectedIndex = 1;
                Check(workspace.Parts.Single().Width == "10", "Unit selection displays inches");
                Check(workspace.Project.Parts.Single().Width == 254, "Unit change retains physical width");
                Field<ComboBox>(window, "UnitInput").SelectedIndex = 0;
                Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(workspace.InventoryPath)), "Project operations leave inventory unchanged");
                var projectPath = Path.Combine(DirectoryPath, "test.panelcut.json");
                await workspace.SaveProjectAsync(projectPath);
                Check((await new ProjectStore().LoadAsync(projectPath)).Parts.Count == 1, "Project saves separately");

                var bladeInput = Field<ComboBox>(window, "BladeInput");
                var kerfDisplay = Field<TextBlock>(window, "KerfDisplay");
                Check(workspace.Project.BladeId is null && kerfDisplay.Text == "Kerf: select a blade", "New project starts without a blade");
                await Invoke<Task>(window, "OptimizeAsync");
                Check(Field<OptimizationResult?>(window, "result") is null && Field<TextBox>(window, "Status").Text.Contains("Select a blade")
                    && Field<TabControl>(window, "Views").SelectedItem == Field<TabItem>(window, "ProjectTab"),
                    "Optimize is blocked until a blade is selected");
                Check(bladeInput.Items.Cast<BladeOption>().Select(option => option.Id).Order().SequenceEqual(new[] { fineId, ripId }.Order()),
                    "Project blade dropdown lists every saved blade");
                bladeInput.SelectedValue = fineId;
                Check(workspace.Project.BladeId == fineId && workspace.IsDirty && kerfDisplay.Text == $"Kerf {EditableRow.Format(3.2)} mm",
                    "Selecting a blade sets the project blade and shows its kerf");
                Field<ComboBox>(window, "UnitInput").SelectedIndex = 1;
                Check(kerfDisplay.Text == $"Kerf {EditableRow.Format(3.2 / 25.4)} inch" && workspace.Project.BladeId == fineId,
                    "Kerf display follows the project unit");
                Field<ComboBox>(window, "UnitInput").SelectedIndex = 0;
                workspace.Parts.Add(workspace.CreatePartRow(new Part(600, 400, oakId, 4) { Label = "Cabinet side", Color = "#B9D4EB" }));
                workspace.Parts.Add(workspace.CreatePartRow(new Part(9999, 9999, oakId) { Label = "Oversize" }));
                Check(Invoke<bool>(window, "CommitProject"), "Mixed project commits");
                await Invoke<Task>(window, "OptimizeAsync");
                await Drain();
                var optimized = Field<OptimizationResult>(window, "result");
                Check(optimized.Sheets.Count > 1, "Optimize creates multiple sheets");
                Check(optimized.UnplacedParts.Count == 1, "Unplaced demand is visible");
                Check(optimized.Settings.BladeId == fineId && optimized.Settings.KerfWidth == 3.2
                    && Field<TextBlock>(window, "Metrics").Text.Contains("Fine crosscut"), "Optimize uses and reports the selected blade kerf");
                Check(Field<Border>(window, "DrawingHost").Child is SheetDrawing, "Selected sheet has a drawing");
                Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(workspace.InventoryPath)), "Optimize never writes inventory");
                Check(workspace.Inventory.Panels[0].Quantity == 4, "Optimize never consumes stock");
                Capture(window, "layout-1280.png");
                Call(window, "NextSheetClick");
                Check(Field<ComboBox>(window, "SheetSelector").SelectedIndex == 1, "Next sheet navigation works");
                Call(window, "PreviousSheetClick");
                window.Width = 960;
                window.Height = 640;
                await Drain();
                Capture(window, "layout-960.png");
                Field<TabControl>(window, "Views").SelectedIndex = 0;
                await Drain();
                Capture(window, "project-960.png");
                bladeInput.SelectedValue = ripId;
                Check(Field<OptimizationResult?>(window, "result") is null && workspace.Project.BladeId == ripId, "Changed inputs invalidate old layout");
                await DeleteBlades(window, [ripId]);
                Check(workspace.Project.BladeId == ripId && kerfDisplay.Text == "Kerf: blade missing"
                    && ((BladeOption)bladeInput.SelectedItem).Display.StartsWith("Missing blade", StringComparison.Ordinal),
                    "Deleting the project's blade shows it as missing without changing the project");
                await Invoke<Task>(window, "OptimizeAsync");
                Check(Field<OptimizationResult?>(window, "result") is null && Field<TextBox>(window, "Status").Text.Contains("was deleted"),
                    "Missing blade blocks optimization");
                Capture(window, "project-missing-blade-960.png");
                bladeInput.SelectedValue = fineId;
                Check(workspace.Project.BladeId == fineId && bladeInput.Items.Cast<BladeOption>().All(option => !option.Display.StartsWith("Missing", StringComparison.Ordinal)),
                    "Selecting another blade clears the missing entry");
                Field<TabControl>(window, "Views").SelectedIndex = 1;
                await Drain();
                Capture(window, "inventory-960.png");

                Call(window, "AddScrapClick");
                await Drain();
                var scrapGrid = Field<DataGrid>(window, "ScrapsGrid");
                var scrapRow = workspace.Scraps.Single();
                SelectMaterial(scrapGrid, scrapRow, birchId);
                Check(scrapRow.Label == "Birch" && scrapRow.Thickness == "12", "Scrap selection fills blank label and thickness");
                SetCell(scrapGrid, scrapRow, "Width", "100");
                SetCell(scrapGrid, scrapRow, "Height", "80");
                SetCell(scrapGrid, scrapRow, "Quantity", "0");
                SetCell(scrapGrid, scrapRow, "EdgeTrim", "50");
                SetCell(scrapGrid, scrapRow, "OriginPanelId", "invalid");
                Check(!scrapGrid.CommitEdit(DataGridEditingUnit.Row, true), "Invalid scrap origin cannot commit");
                SetCell(scrapGrid, scrapRow, "OriginPanelId", "");
                Check(scrapGrid.CommitEdit(DataGridEditingUnit.Row, true), "Valid depleted scrap commits");
                await Field<Task>(window, "stockSave");
                Check(workspace.Inventory.Scraps.Single().Quantity == 0 && !workspace.Inventory.Scraps[0].IsUsable, "Zero quantity and unusable trim persist");
                SelectMaterial(scrapGrid, scrapRow, oakId);
                Check(scrapRow.Label == "Birch", "Reselecting material preserves nonblank label");
                Check(scrapGrid.CommitEdit(DataGridEditingUnit.Row, true), "Scrap material change commits");
                await Field<Task>(window, "stockSave");
                before = await File.ReadAllBytesAsync(workspace.InventoryPath);
                var projectBefore = await File.ReadAllBytesAsync(projectPath);
                var dirtyBefore = workspace.IsDirty;
                SelectTab(window, "MaterialsTab");
                await Drain();
                oakRow = workspace.Materials.Single(material => material.Id == oakId);
                using (var locked = new FileStream(workspace.MaterialsPath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    SetCell(materialsGrid, oakRow, "Thickness", "19");
                    materialsGrid.CommitEdit(DataGridEditingUnit.Row, true);
                    await Field<Task>(window, "materialSave");
                    Check(workspace.Catalogue.Resolve(oakId).Thickness == 18 && workspace.Panels[0].Thickness == "18", "Failed catalogue save leaves effective material unchanged");
                    Check(Field<bool>(window, "materialPending"), "Failed catalogue save remains pending");
                }
                Check(Enumerable.SequenceEqual(materialBytes, await File.ReadAllBytesAsync(workspace.MaterialsPath)), "Failed material save preserves bytes");
                Call(window, "RetryMaterialClick");
                await Drain();
                await Field<Task>(window, "materialSave");
                Check(workspace.Panels[0].Thickness == "19" && workspace.Scraps[0].Thickness == "19" && workspace.Parts[0].Thickness == "19", "Catalogue thickness change updates panels scraps and parts");
                SetCell(materialsGrid, oakRow, "Name", "Oak renamed");
                materialsGrid.CommitEdit(DataGridEditingUnit.Row, true);
                await Field<Task>(window, "materialSave");
                Check(workspace.Panels[0].Label == "Oak" && workspace.Scraps[0].Label == "Birch", "Catalogue rename does not rewrite stock labels");
                Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(workspace.InventoryPath)), "Catalogue edits never write inventory");
                Check(Enumerable.SequenceEqual(projectBefore, await File.ReadAllBytesAsync(projectPath)) && dirtyBefore == workspace.IsDirty, "Catalogue edits never write or dirty project");
                await VerifyTableLayoutAsync(window);
                var reopened = new WorkspaceViewModel(workspace.InventoryPath, workspace.MaterialsPath);
                await reopened.LoadMaterialsAsync();
                await reopened.LoadInventoryAsync();
                await reopened.OpenProjectAsync(projectPath);
                Check(reopened.Panels[0].Thickness == "19" && reopened.Parts[0].Thickness == "19", "Reopened files use latest catalogue details");
                var draft = workspace.CreateStockRow(false, new Panel(100, 80, oakId));
                Check(draft.Label == "", "Loading blank stock labels does not autofill");
                draft.BeginEdit();
                draft.MaterialId = birchId;
                Check(draft.Label == "Birch", "Draft selection fills blank label");
                draft.CancelEdit();
                Check(draft.Label == "" && draft.MaterialId == oakId, "Cancel restores material and original blank label without autofill");
                Field<TabControl>(window, "Views").SelectedIndex = 1;
                await DeleteStock(window, [scrapRow.Id]);
                Check((await new InventoryStore(workspace.InventoryPath).LoadAsync()).Scraps.Count == 0, "Manual delete persists");
                before = await File.ReadAllBytesAsync(workspace.InventoryPath);
                try
                {
                    await workspace.SaveProjectAsync(workspace.InventoryPath);
                    throw new Exception("Project overwrote inventory");
                }
                catch (InvalidOperationException) { Console.WriteLine("PASS Project cannot overwrite inventory path"); }
                try
                {
                    await workspace.SaveProjectAsync(workspace.MaterialsPath);
                    throw new Exception("Project overwrote materials");
                }
                catch (InvalidOperationException) { Console.WriteLine("PASS Project cannot overwrite materials path"); }
                try
                {
                    await workspace.SaveProjectAsync(workspace.BladesPath);
                    throw new Exception("Project overwrote blades");
                }
                catch (InvalidOperationException) { Console.WriteLine("PASS Project cannot overwrite blades path"); }
                Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(workspace.InventoryPath)), "Rejected project save preserves inventory");
                var currentProject = workspace.Project;
                var currentPath = workspace.ProjectPath;
                var corruptPath = Path.Combine(DirectoryPath, "corrupt.panelcut.json");
                await File.WriteAllTextAsync(corruptPath, "{");
                try { await workspace.OpenProjectAsync(corruptPath); }
                catch (InvalidDataException) { }
                Check(ReferenceEquals(currentProject, workspace.Project) && currentPath == workspace.ProjectPath, "Failed project open retains current document");
                await workspace.OpenProjectAsync(projectPath);
                Check(!workspace.IsDirty && workspace.Parts.Count == 3, "Opening saved project resets dirty state");
                workspace.NewProject();
                Check(workspace.Parts.Count == 0 && workspace.Project.Unit == LengthUnit.Millimetres && workspace.Project.BladeId is null, "New project restores defaults");
                Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(workspace.InventoryPath)), "Open and New preserve stock file");
                await File.WriteAllTextAsync(workspace.InventoryPath, "{");
                await Invoke<Task>(window, "LoadStockAsync");
                Check(!workspace.InventoryReady && !Field<DataGrid>(window, "PanelsGrid").IsEnabled, "Corrupt stock disables inventory editing");
                Check(!Field<Button>(window, "OptimizeButton").IsEnabled, "Corrupt stock disables Optimize");
                Check(await File.ReadAllTextAsync(workspace.InventoryPath) == "{", "Corrupt stock is never overwritten on load");
                await File.WriteAllBytesAsync(workspace.InventoryPath, before);
                await Invoke<Task>(window, "LoadStockAsync");
                Check(workspace.InventoryReady && workspace.Inventory.Panels.Count == 1, "Inventory reload recovers after file repair");
                var catalogueBefore = await File.ReadAllBytesAsync(workspace.MaterialsPath);
                await File.WriteAllTextAsync(workspace.MaterialsPath, "{");
                await Invoke<Task>(window, "LoadMaterialsAsync");
                Check(!workspace.MaterialsReady && !Field<DataGrid>(window, "PartsGrid").IsEnabled
                    && !Field<Button>(window, "AddPartButton").IsEnabled, "Corrupt catalogue blocks material-dependent editing");
                Check(await File.ReadAllTextAsync(workspace.MaterialsPath) == "{", "Corrupt catalogue is never overwritten on load");
                await File.WriteAllBytesAsync(workspace.MaterialsPath, catalogueBefore);
                await Invoke<Task>(window, "LoadMaterialsAsync");
                Check(workspace.MaterialsReady, "Catalogue reload recovers after repair");
                var bladesBefore = await File.ReadAllBytesAsync(workspace.BladesPath);
                await File.WriteAllTextAsync(workspace.BladesPath, "{");
                await Invoke<Task>(window, "LoadBladesAsync");
                Check(!workspace.BladesReady && !Field<DataGrid>(window, "BladesGrid").IsEnabled && !Field<ComboBox>(window, "BladeInput").IsEnabled
                    && !Field<Button>(window, "OptimizeButton").IsEnabled, "Corrupt blades file blocks blade editing and Optimize");
                Check(await File.ReadAllTextAsync(workspace.BladesPath) == "{", "Corrupt blades file is never overwritten on load");
                await File.WriteAllBytesAsync(workspace.BladesPath, bladesBefore);
                await Invoke<Task>(window, "LoadBladesAsync");
                Check(workspace.BladesReady && workspace.Blades.Blades.Count == 1, "Blades reload recovers after repair");
                await File.WriteAllTextAsync(workspace.InventoryPath, """{"schemaVersion":1,"panels":[],"scraps":[]}""");
                await Invoke<Task>(window, "LoadStockAsync");
                Check(!workspace.InventoryReady, "Old inventory format is rejected");
                await File.WriteAllBytesAsync(workspace.InventoryPath, before);
                await Invoke<Task>(window, "LoadStockAsync");
                await new MaterialStore(workspace.MaterialsPath).SaveAsync(new MaterialCatalogue());
                await Invoke<Task>(window, "LoadMaterialsAsync");
                Check(workspace.Inventory.Panels[0].MaterialId == oakId && workspace.Panels[0].MaterialId == oakId
                    && workspace.Panels[0].Usability == "Select material", "Missing references remain visible and retain their IDs");
                workspace.Parts.Add(workspace.CreatePartRow(new Part(100, 100, oakId)));
                Invoke<object?>(window, "RefreshProjectControls");
                Field<ComboBox>(window, "BladeInput").SelectedValue = fineId;
                Check(workspace.ProjectBlade?.Id == fineId, "Blade is selected before the unresolved-material check");
                await Invoke<Task>(window, "OptimizeAsync");
                Check(Field<OptimizationResult?>(window, "result") is null, "Unresolved material blocks optimization");
                Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(workspace.InventoryPath)), "Unresolved references do not rewrite inventory");
                await File.WriteAllBytesAsync(workspace.MaterialsPath, catalogueBefore);
                await Invoke<Task>(window, "LoadMaterialsAsync");
                Check(workspace.Panels[0].Thickness == "19", "Restored catalogue resolves preserved IDs");
                SelectTab(window, "MaterialsTab");
                await Drain();
                Capture(window, "materials-960.png");
                await VerifyStockTabsAsync(window, workspace, oakId, birchId);
                await VerifyPartAutosaveAsync(window, workspace, oakId);
                Console.WriteLine("Desktop editing checks passed.");
                Console.WriteLine($"Artifacts: {DirectoryPath}");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                Environment.ExitCode = 1;
            }
            finally
            {
                if (window is not null)
                {
                    typeof(MainWindow).GetField("allowClose", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                    window.Close();
                }
                application.Shutdown(Environment.ExitCode);
            }
        };
        application.Run();
    }

    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(instance)!;

    private static void SelectTab(MainWindow window, string name) =>
        Field<TabControl>(window, "Views").SelectedItem = Field<TabItem>(window, name);

    private static Task DeleteStock(MainWindow window, Guid[] ids) =>
        (Task)typeof(MainWindow).GetMethod("DeleteStockAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [ids])!;

    private static Task DeleteBlades(MainWindow window, Guid[] ids) =>
        (Task)typeof(MainWindow).GetMethod("DeleteBladesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [ids])!;

    private static Task DeleteBrands(MainWindow window, Guid[] ids) =>
        (Task)typeof(MainWindow).GetMethod("DeleteBrandsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [ids])!;

    private static void PressKey(UIElement target, Key key) =>
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        });

    private static async Task<BrandPicker> BeginBrand(DataGrid grid, BladeRow row)
    {
        var column = grid.Columns.Single(column => column.SortMemberPath == "Brand");
        grid.SelectedItem = row;
        grid.ScrollIntoView(row, column);
        grid.CurrentCell = new DataGridCellInfo(row, column);
        grid.BeginEdit();
        grid.UpdateLayout();
        await Drain();
        return Find<BrandPicker>(column.GetCellContent(row)) ?? throw new InvalidOperationException("No brand picker");
    }

    // Keyboard focus is not guaranteed in the harness, so open the list explicitly when typing did not.
    private static void OpenBrandList(BrandPicker picker)
    {
        if (!picker.IsDropDownOpen)
            PressKey(picker, Key.Down);
        Check(picker.IsDropDownOpen, "Brand dropdown opens");
    }

    private static async Task SaveBladeRow(MainWindow window, DataGrid grid, string description)
    {
        Check(grid.CommitEdit(DataGridEditingUnit.Row, true), description);
        await Field<Task>(window, "bladeSave");
        await Drain();
        Check(!Field<bool>(window, "bladePending"), $"{description} and saves");
    }

    private static async Task<(Guid Fine, Guid Rip)> VerifyBladesAsync(MainWindow window, WorkspaceViewModel workspace)
    {
        SelectTab(window, "BladesTab");
        await Drain();
        Check(workspace.BladesReady && workspace.BladeRows.Count == 0 && !File.Exists(workspace.BladesPath),
            "Missing blades file loads empty without creating it");
        var grid = Field<DataGrid>(window, "BladesGrid");
        var brandsGrid = Field<DataGrid>(window, "BrandsGrid");
        Check(grid.SelectionMode == DataGridSelectionMode.Extended && brandsGrid.SelectionMode == DataGridSelectionMode.Extended,
            "Blade and brand grids support multiple selection");

        Call(window, "AddBladeClick");
        await Drain();
        var fine = workspace.BladeRows.Single();
        SetCell(grid, fine, "Name", "Fine crosscut");
        var picker = await BeginBrand(grid, fine);
        Check(picker.Choices.Count == 0, "Brand dropdown is empty before any brand exists");
        picker.Text = "Freud";
        Check(picker.Choices.Count == 1 && picker.Choices[0].IsNew && picker.Choices[0].Label == "Create \"Freud\"",
            "Typing an unknown brand offers to create it");
        OpenBrandList(picker);
        PressKey(picker, Key.Enter);
        Check(!picker.IsDropDownOpen && fine.NewBrand == "Freud" && fine.BrandId is null, "Enter chooses Create for a new brand");
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        SetCell(grid, fine, "BrandCode", "LU3D 1000");
        SetCell(grid, fine, "Diameter", "250");
        SetCell(grid, fine, "Teeth", "80");
        SetCell(grid, fine, "Kerf", EditableRow.Format(3.2));
        await SaveBladeRow(window, grid, "Blade with a new brand commits");
        var saved = await new BladeStore(workspace.BladesPath).LoadAsync();
        var freud = saved.Brands.Single();
        Check(freud.Name == "Freud" && saved.Blades.Single().BrandId == freud.Id && saved.Blades[0].Kerf == 3.2
            && saved.Blades[0].BrandCode == "LU3D 1000", "New brand and blade are saved together");
        Check(workspace.BrandNames.SequenceEqual(new[] { "Freud" }) && workspace.BrandRows.Single().Id == freud.Id,
            "Created brand becomes a dropdown option and appears in Brands");

        Call(window, "AddBladeClick");
        await Drain();
        var rip = workspace.BladeRows.Last();
        SetCell(grid, rip, "Name", "Rip");
        picker = await BeginBrand(grid, rip);
        Check(picker.Choices.Count == 1 && !picker.Choices[0].IsNew, "Opening the brand editor lists existing brands");
        picker.Text = "fre";
        Check(picker.Choices.Select(choice => choice.Label).SequenceEqual(new[] { "Freud", "Create \"fre\"" }),
            "Partial text filters brands and still offers Create");
        OpenBrandList(picker);
        PressKey(picker, Key.Enter);
        Check(rip.BrandId == freud.Id && rip.NewBrand == "" && picker.Text == "Freud", "Enter chooses the highlighted existing brand");
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        SetCell(grid, rip, "Diameter", "300");
        SetCell(grid, rip, "Teeth", "24");
        SetCell(grid, rip, "Kerf", "0");
        await SaveBladeRow(window, grid, "Blade with an existing brand and zero kerf commits");
        rip = workspace.BladeRows.Single(row => row.Id == rip.Id);
        SetCell(grid, rip, "Brand", "FREUD");
        await SaveBladeRow(window, grid, "Brand typed in another case commits");
        Check(workspace.Blades.Brands.Count == 1 && workspace.Blades.Resolve(rip.Id).BrandId == freud.Id,
            "Brand names match ignoring case instead of creating duplicates");

        var before = await File.ReadAllBytesAsync(workspace.BladesPath);
        Call(window, "AddBladeClick");
        await Drain();
        var draft = workspace.BladeRows.Last();
        SetCell(grid, draft, "Name", "Draft");
        SetCell(grid, draft, "Brand", "Makita");
        SetCell(grid, draft, "Diameter", "0");
        SetCell(grid, draft, "Teeth", "24");
        SetCell(grid, draft, "Kerf", "2");
        Check(!grid.CommitEdit(DataGridEditingUnit.Row, true), "Invalid blade diameter cannot commit");
        SetCell(grid, draft, "Diameter", "250");
        SetCell(grid, draft, "Name", "RIP");
        Check(!grid.CommitEdit(DataGridEditingUnit.Row, true), "Duplicate blade names are rejected ignoring case");
        Call(window, "CancelBladeClick");
        await Drain();
        Check(workspace.BladeRows.Count == 2 && workspace.BrandRows.Count == 1 && !Field<bool>(window, "bladePending")
            && Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(workspace.BladesPath)),
            "Cancelling a draft with a new brand creates neither the blade nor the brand");

        var brandRow = workspace.BrandRows.Single();
        SetCell(brandsGrid, brandRow, "Name", "Freud Tools");
        await SaveBladeRow(window, brandsGrid, "Brand rename commits");
        Check(workspace.BladeRows.All(row => row.Brand == "Freud Tools") && workspace.BrandNames.Single() == "Freud Tools",
            "Renaming a brand updates every blade using it");
        before = await File.ReadAllBytesAsync(workspace.BladesPath);
        await DeleteBrands(window, [freud.Id]);
        Check(workspace.Blades.Brands.Count == 1 && Field<TextBox>(window, "Status").Text.Contains("used by 2 blade(s)")
            && Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(workspace.BladesPath)), "A brand in use cannot be deleted");
        Call(window, "AddBrandClick");
        await Drain();
        SetCell(brandsGrid, workspace.BrandRows.Last(), "Name", "freud tools");
        Check(!brandsGrid.CommitEdit(DataGridEditingUnit.Row, true), "Duplicate brand names are rejected ignoring case");
        SetCell(brandsGrid, workspace.BrandRows.Last(), "Name", "Festool");
        await SaveBladeRow(window, brandsGrid, "Unused brand commits");
        var festool = workspace.Blades.Brands.Single(brand => brand.Name == "Festool");
        await DeleteBrands(window, [festool.Id]);
        Check(workspace.Blades.Brands.Select(brand => brand.Name).SequenceEqual(new[] { "Freud Tools" }), "An unused brand can be deleted");

        foreach (var width in new[] { 1280, 960 })
        {
            window.Width = width;
            await Drain();
            Capture(window, $"blades-{width}.png");
        }
        window.Width = 1280;
        picker = await BeginBrand(grid, workspace.BladeRows[0]);
        picker.Text = "F";
        OpenBrandList(picker);
        var popup = (Popup)typeof(BrandPicker).GetField("popup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(picker)!;
        await Drain();
        CaptureElement((FrameworkElement)popup.Child, "brand-picker-popup.png");
        Call(window, "CancelBladeClick");
        await Drain();
        return (workspace.BladeRows.Single(row => row.Name == "Fine crosscut").Id, workspace.BladeRows.Single(row => row.Name == "Rip").Id);
    }

    private static Task<bool> SaveProject(MainWindow window) =>
        (Task<bool>)typeof(MainWindow).GetMethod("SaveProjectAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [false])!;

    private static async Task SelectStockMaterial(MainWindow window, bool scrap, Guid materialId)
    {
        SelectTab(window, scrap ? "ScrapsTab" : "PanelsTab");
        var tabs = Field<TabControl>(window, scrap ? "ScrapMaterialTabs" : "PanelMaterialTabs");
        tabs.SelectedItem = tabs.Items.Cast<MaterialOption>().Single(option => option.Id == materialId);
        await Field<Task>(window, "stockSave");
        await Drain();
        var grid = Field<DataGrid>(window, scrap ? "ScrapsGrid" : "PanelsGrid");
        Check(grid.Columns.OfType<DataGridComboBoxColumn>().Single().Visibility ==
            (materialId == Guid.Empty ? Visibility.Visible : Visibility.Collapsed),
            $"{grid.Name} shows Material only in All");
    }

    private static async Task<StockRow> AddStockRow(MainWindow window, WorkspaceViewModel workspace, bool scrap, Guid materialId)
    {
        await SelectStockMaterial(window, scrap, materialId);
        Call(window, scrap ? "AddScrapClick" : "AddPanelClick");
        var row = (scrap ? workspace.Scraps : workspace.Panels).Last();
        var grid = Field<DataGrid>(window, scrap ? "ScrapsGrid" : "PanelsGrid");
        Check(row.MaterialId == materialId && grid.Items.Contains(row), "New stock inherits the active material and stays visible");
        SetCell(grid, row, "Width", "300");
        SetCell(grid, row, "Height", "200");
        Check(grid.CommitEdit(DataGridEditingUnit.Row, true), "New filtered stock row commits");
        await Field<Task>(window, "stockSave");
        await Drain();
        var saved = await new InventoryStore(workspace.InventoryPath).LoadAsync();
        Check((scrap ? saved.Scraps.Select(stock => stock.Id) : saved.Panels.Select(stock => stock.Id)).Contains(row.Id),
            "New panel or scrap saves automatically without pressing Save");
        return row;
    }

    private static async Task VerifyStockTabsAsync(MainWindow window, WorkspaceViewModel workspace, Guid oakId, Guid birchId)
    {
        foreach (var name in new[] { "RetryEditButton", "SaveScrapsButton", "CommitMaterialButton" })
            Check((string)Field<Button>(window, name).Content == "Save", $"{name} uses Save wording");
        var panelTabs = Field<TabControl>(window, "PanelMaterialTabs");
        var scrapTabs = Field<TabControl>(window, "ScrapMaterialTabs");
        Check(panelTabs.Items.Count == workspace.MaterialOptions.Count + 1 && scrapTabs.Items.Count == panelTabs.Items.Count,
            "Both stock views have All plus every catalogue material");
        foreach (var tabs in new[] { panelTabs, scrapTabs })
            Check(tabs.Items.Cast<MaterialOption>().Where(option => option.Id != Guid.Empty)
                .All(option => option.Display == workspace.Catalogue.Resolve(option.Id).Name),
                "Material tabs use only catalogue names, including renames");
        await SelectStockMaterial(window, false, oakId);
        await SelectStockMaterial(window, true, birchId);
        Check(((MaterialOption)panelTabs.SelectedItem).Id == oakId && ((MaterialOption)scrapTabs.SelectedItem).Id == birchId,
            "Panel and scrap material selections are independent");
        Check(Field<DataGrid>(window, "ScrapsGrid").Items.Count == 0, "Empty material groups remain available for additions");

        foreach (var scrap in new[] { false, true })
        {
            var grid = Field<DataGrid>(window, scrap ? "ScrapsGrid" : "PanelsGrid");
            var tabs = scrap ? scrapTabs : panelTabs;
            Check(grid.SelectionMode == DataGridSelectionMode.Extended && grid.SelectionUnit == DataGridSelectionUnit.FullRow,
                "Stock grids support multiple full-row selection");
            var first = await AddStockRow(window, workspace, scrap, oakId);
            var second = await AddStockRow(window, workspace, scrap, oakId);
            var other = await AddStockRow(window, workspace, scrap, birchId);
            await SelectStockMaterial(window, scrap, oakId);
            Check(!grid.Items.Contains(other) && grid.Items.Contains(first) && grid.Items.Contains(second),
                "Material filter shows only matching stock without removing hidden rows");

            var before = await File.ReadAllBytesAsync(workspace.InventoryPath);
            Call(window, scrap ? "AddScrapClick" : "AddPanelClick");
            var draft = (scrap ? workspace.Scraps : workspace.Panels).Last();
            tabs.SelectedItem = tabs.Items.Cast<MaterialOption>().Single(option => option.Id == birchId);
            await Drain();
            Check(((MaterialOption)tabs.SelectedItem).Id == oakId && grid.Items.Contains(draft),
                "Invalid draft prevents switching material tabs and stays visible");
            Check(grid.Columns.OfType<DataGridComboBoxColumn>().Single().Visibility == Visibility.Collapsed,
                "Rejected material switch keeps Material hidden");
            Call(window, "CancelStockClick");
            await Drain();
            Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(workspace.InventoryPath)),
                "Cancelled filtered addition leaves inventory bytes unchanged");

            var rows = scrap ? workspace.Scraps : workspace.Panels;
            first = rows.Single(row => row.Id == first.Id);
            second = rows.Single(row => row.Id == second.Id);
            await SelectStockMaterial(window, scrap, Guid.Empty);
            SelectMaterial(grid, first, birchId);
            Check(grid.CommitEdit(DataGridEditingUnit.Row, true), "Material reassignment commits in All");
            await Field<Task>(window, "stockSave");
            await SelectStockMaterial(window, scrap, oakId);
            Check(!grid.Items.Contains(first), "Saved reassignment removes a row from its old material tab");
            await SelectStockMaterial(window, scrap, birchId);
            Check(grid.Items.Contains(first), "Reassigned stock appears in its new material tab");
            await SelectStockMaterial(window, scrap, Guid.Empty);
            SelectMaterial(grid, first, oakId);
            Check(grid.CommitEdit(DataGridEditingUnit.Row, true), "Material reassignment back commits");
            await Field<Task>(window, "stockSave");
            await SelectStockMaterial(window, scrap, oakId);
            grid.UnselectAll();
            grid.SelectedItems.Add(first);
            grid.SelectedItems.Add(second);
            Check(grid.SelectedItems.Count == 2, "Two stock rows can be selected together");
            var ids = grid.SelectedItems.Cast<StockRow>().Select(row => row.Id).ToArray();
            window.Width = 1280;
            window.Height = 800;
            await Drain();
            Capture(window, scrap ? "scraps-1280.png" : "panels-1280.png");
            window.Width = 960;
            window.Height = 640;
            await Drain();
            Capture(window, scrap ? "scraps-960.png" : "panels-960.png");
            before = await File.ReadAllBytesAsync(workspace.InventoryPath);
            using (var locked = new FileStream(workspace.InventoryPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                await DeleteStock(window, ids);
                Check(Field<bool>(window, "stockPending") && rows.Any(row => row.Id == first.Id) && rows.Any(row => row.Id == second.Id),
                    "Failed bulk delete preserves committed rows and remains pending");
            }
            Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(workspace.InventoryPath)),
                "Failed bulk delete preserves inventory bytes");
            Call(window, "RetryStockClick");
            await Field<Task>(window, "stockSave");
            await Drain();
            Check(!Field<bool>(window, "stockPending") && rows.All(row => !ids.Contains(row.Id)) && rows.Any(row => row.Id == other.Id),
                "Save retries the whole deletion and preserves unselected hidden rows");
            var saved = await new InventoryStore(workspace.InventoryPath).LoadAsync();
            var savedIds = (scrap ? saved.Scraps.Select(row => row.Id) : saved.Panels.Select(row => row.Id)).ToArray();
            Check(savedIds.All(id => !ids.Contains(id)) && savedIds.Contains(other.Id), "Bulk deletion persists selected IDs only");
            await SelectStockMaterial(window, scrap, Guid.Empty);
            Check(grid.Items.Count == rows.Count, "All restores the complete stock list");
        }

        var duplicate = new Material("OAK RENAMED", "Plywood", 19);
        var longName = new Material("Decorative laminated furniture panel with a long material name", "Laminated plywood", 22);
        workspace.Materials.Add(new MaterialRow(duplicate));
        var duplicateRejected = false;
        try { workspace.MaterialCandidate(); }
        catch (ArgumentException) { duplicateRejected = true; }
        Check(duplicateRejected, "Duplicate material names are rejected ignoring case");
        workspace.Materials.RemoveAt(workspace.Materials.Count - 1);
        workspace.Materials.Add(new MaterialRow(longName));
        await workspace.CommitManualMaterialEditAsync(workspace.MaterialCandidate());
        await SelectStockMaterial(window, false, oakId);
        await Invoke<Task>(window, "LoadMaterialsAsync");
        Check(((MaterialOption)panelTabs.SelectedItem).Id == oakId, "Catalogue reload preserves material tab selection by ID");
        Check(workspace.MaterialOptions.All(option => option.Display == workspace.Catalogue.Resolve(option.Id).Name),
            "Material dropdowns list names only");
        await SelectStockMaterial(window, false, longName.Id);
        Check(Field<DataGrid>(window, "PanelsGrid").Items.Count == 0, "Unused material tab shows no stock");
        foreach (var width in new[] { 1280, 960 })
        {
            window.Width = width;
            await Drain();
            Capture(window, $"material-tabs-{width}.png");
        }
        var catalogueBytes = await File.ReadAllBytesAsync(workspace.MaterialsPath);
        await new MaterialStore(workspace.MaterialsPath).SaveAsync(new MaterialCatalogue());
        await Invoke<Task>(window, "LoadMaterialsAsync");
        Check(((MaterialOption)panelTabs.SelectedItem).Id == Guid.Empty && Field<DataGrid>(window, "PanelsGrid").Items.Count == workspace.Panels.Count,
            "Removed catalogue material falls back to All and unresolved stock remains visible");
        Check(Field<DataGrid>(window, "PanelsGrid").Columns.OfType<DataGridComboBoxColumn>().Single().Visibility == Visibility.Visible,
            "Catalogue fallback restores the Material column");
        await File.WriteAllBytesAsync(workspace.MaterialsPath, catalogueBytes);
        await Invoke<Task>(window, "LoadMaterialsAsync");
    }

    private static PartRow BeginPart(MainWindow window, WorkspaceViewModel workspace, Guid materialId)
    {
        SelectTab(window, "ProjectTab");
        Call(window, "AddPartClick");
        var row = workspace.Parts.Last();
        var grid = Field<DataGrid>(window, "PartsGrid");
        SelectMaterial(grid, row, materialId);
        SetCell(grid, row, "Width", "150");
        SetCell(grid, row, "Height", "100");
        return row;
    }

    private static async Task VerifyPartAutosaveAsync(MainWindow window, WorkspaceViewModel workspace, Guid materialId)
    {
        workspace.NewProject();
        Invoke<object?>(window, "RefreshProjectControls");
        var grid = Field<DataGrid>(window, "PartsGrid");
        var inventoryBytes = await File.ReadAllBytesAsync(workspace.InventoryPath);
        var materialBytes = await File.ReadAllBytesAsync(workspace.MaterialsPath);
        var first = BeginPart(window, workspace, materialId);
        Check(grid.CommitEdit(DataGridEditingUnit.Row, true), "Untitled project accepts a valid new part");
        await Field<Task>(window, "projectSave");
        Check(workspace.ProjectPath is null && workspace.IsDirty, "Untitled additions remain unsaved without opening a dialog");
        var path = Path.Combine(DirectoryPath, "autosave.panelcut.json");
        await workspace.SaveProjectAsync(path);

        var second = BeginPart(window, workspace, materialId);
        Check(grid.CommitEdit(DataGridEditingUnit.Row, true), "Existing project accepts a new part");
        await Field<Task>(window, "projectSave");
        await Drain();
        Check((await new ProjectStore().LoadAsync(path)).Parts.Any(part => part.Id == second.Id) && !workspace.IsDirty,
            "New part automatically saves to the existing project and clears dirty state");
        var before = await File.ReadAllBytesAsync(path);
        SetCell(grid, second, "Label", "Edited existing part");
        Check(grid.CommitEdit(DataGridEditingUnit.Row, true), "Existing part edit commits");
        await Field<Task>(window, "projectSave");
        Check(workspace.IsDirty && Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(path)),
            "Existing part edits retain manual saving");

        var invalid = BeginPart(window, workspace, materialId);
        SetCell(grid, invalid, "Width", "-1");
        Check(!grid.CommitEdit(DataGridEditingUnit.Row, true), "Invalid new part is rejected before autosave");
        Call(window, "CancelPartClick");
        await Drain();
        Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(path)) && workspace.Parts.Count == 2,
            "Invalid and cancelled additions leave the project file unchanged");

        PartRow failed;
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            failed = BeginPart(window, workspace, materialId);
            Check(grid.CommitEdit(DataGridEditingUnit.Row, true), "New part remains committed when autosave fails");
            await Field<Task>(window, "projectSave");
            Check(workspace.IsDirty && workspace.Project.Parts.Any(part => part.Id == failed.Id)
                && Field<TextBox>(window, "Status").Text.Contains("Project NOT saved"),
                "Autosave failure keeps the new part dirty and reports a Save retry");
        }
        Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(path)), "Failed part autosave preserves project bytes");
        Check(await SaveProject(window) && !workspace.IsDirty, "Manual Save retries a failed new-part autosave");
        Check((await new ProjectStore().LoadAsync(path)).Parts.Count == 3, "Retry persists all committed parts");

        before = await File.ReadAllBytesAsync(path);
        grid.UnselectAll();
        grid.SelectedItems.Add(workspace.Parts.Single(part => part.Id == first.Id));
        grid.SelectedItems.Add(workspace.Parts.Single(part => part.Id == second.Id));
        Check(grid.SelectionMode == DataGridSelectionMode.Extended && grid.SelectedItems.Count == 2, "Parts support multiple selection");
        Call(window, "DeletePartClick");
        Check(workspace.Parts.Count == 1 && workspace.Project.Parts.Single().Id == failed.Id && workspace.IsDirty,
            "Part bulk delete removes selected rows and retains the unselected row");
        Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(path)), "Part deletion retains manual saving");

        BeginPart(window, workspace, materialId);
        Check(grid.CommitEdit(DataGridEditingUnit.Row, true), "Next new part schedules autosave");
        Check(await SaveProject(window), "Manual Save waits for an in-flight autosave");
        Check((await new ProjectStore().LoadAsync(path)).Parts.Count == 2 && !workspace.IsDirty,
            "Serialized saving persists the complete current project");
        BeginPart(window, workspace, materialId);
        Check(grid.CommitEdit(DataGridEditingUnit.Row, true), "New project navigation test starts autosave");
        await Invoke<Task>(window, "NewProjectAsync");
        Check(workspace.ProjectPath is null && workspace.Parts.Count == 0 && (await new ProjectStore().LoadAsync(path)).Parts.Count == 3,
            "New Project waits for autosave before replacing the current document");
        Check(Enumerable.SequenceEqual(inventoryBytes, await File.ReadAllBytesAsync(workspace.InventoryPath))
            && Enumerable.SequenceEqual(materialBytes, await File.ReadAllBytesAsync(workspace.MaterialsPath)),
            "Part autosave and bulk delete never write inventory or materials");
    }

    private static bool IsNumericProperty(string property) =>
        property is "Width" or "Height" or "Quantity" or "Thickness" or "Priority" or "CostPerUnit" or "EdgeTrim"
            or "Diameter" or "Teeth" or "Kerf";

    private static async Task VerifyTableLayoutAsync(MainWindow window)
    {
        foreach (var width in new[] { 1280, 960 })
        {
            window.Width = width;
            foreach (var (tabName, gridName) in new[]
            {
                ("ProjectTab", "PartsGrid"), ("PanelsTab", "PanelsGrid"),
                ("ScrapsTab", "ScrapsGrid"), ("MaterialsTab", "MaterialsGrid"),
                ("BladesTab", "BladesGrid"), ("BladesTab", "BrandsGrid")
            })
            {
                SelectTab(window, tabName);
                await Drain();
                window.UpdateLayout();
                var grid = Field<DataGrid>(window, gridName);
                var row = grid.Items[0];
                foreach (var column in grid.Columns)
                {
                    grid.ScrollIntoView(row, column);
                    grid.UpdateLayout();
                    var content = column.GetCellContent(row);
                    if (column is DataGridTemplateColumn && row is PartRow part && Find<Button>(content) is { } swatch)
                        Check(swatch.Content is Border { Background: SolidColorBrush brush } && brush.Color.ToString() == $"#FF{part.Color[1..]}",
                            $"{gridName} colour swatch shows the part colour");
                    else if (column is DataGridTextColumn || column is DataGridTemplateColumn)
                    {
                        var property = column is DataGridTextColumn textColumn
                            ? ((Binding)textColumn.Binding).Path.Path : column.SortMemberPath;
                        var text = content as TextBlock ?? Find<TextBlock>(content);
                        Check(text is not null && text.VerticalAlignment == VerticalAlignment.Center
                            && text.TextAlignment == (IsNumericProperty(property) ? TextAlignment.Right : TextAlignment.Left),
                            $"{gridName}.{property} is centered vertically and aligned by value type at {width}");
                    }
                    else if (content is ComboBox combo)
                        Check(combo.VerticalContentAlignment == VerticalAlignment.Center && combo.HorizontalContentAlignment == HorizontalAlignment.Left,
                            $"{gridName} material display is centered vertically and left-aligned");
                    else if (content is CheckBox checkBox)
                        Check(checkBox.VerticalAlignment == VerticalAlignment.Center, "Checkbox cells are centered vertically");
                }
                var scroll = Find<ScrollViewer>(grid)!;
                scroll.ScrollToLeftEnd();
                await Drain();
                Capture(window, $"{gridName}-{width}.png");
                var lastColumn = grid.Columns.OrderBy(column => column.DisplayIndex).Last();
                var originalWidth = lastColumn.Width;
                if (scroll.ScrollableWidth == 0)
                    lastColumn.Width = new DataGridLength(grid.ActualWidth + 100);
                await Drain();
                grid.UpdateLayout();
                Check(scroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto
                    && scroll.ComputedHorizontalScrollBarVisibility == Visibility.Visible && scroll.ScrollableWidth > 0,
                    $"{gridName} offers a horizontal scrollbar for overflowing columns at {width}");
                scroll.ScrollToRightEnd();
                await Drain();
                Check(scroll.HorizontalOffset > 0 && Math.Abs(scroll.HorizontalOffset - scroll.ScrollableWidth) < 1,
                    $"{gridName} scrolls to the right edge at {width}");
                Capture(window, $"{gridName}-scrolled-{width}.png");
                lastColumn.Width = originalWidth;
                scroll.ScrollToLeftEnd();
                await Drain();
            }
        }
    }

    private static void Call(MainWindow window, string name) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [window, new RoutedEventArgs()]);

    private static T Invoke<T>(MainWindow window, string name) =>
        (T)typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;

    private static void Capture(MainWindow window, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(DirectoryPath, name));
        encoder.Save(output);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        Check(pixels.Any(value => value != 0), $"Screenshot is nonblank: {name}");
    }

    private static void CaptureElement(FrameworkElement element, string name)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(DirectoryPath, name));
        encoder.Save(output);
    }

    private static async Task Drain() => await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

    private static void VerifyThicknessInput(PositiveNumberInput editor)
    {
        var increase = Find<RepeatButton>(editor)!;
        var decrease = (RepeatButton)((Grid)increase.Parent).Children[1];
        Check(increase.Command == PositiveNumberInput.Increase && decrease.Command == PositiveNumberInput.Decrease
            && increase.CommandTarget == editor && decrease.CommandTarget == editor,
            "Both arrow buttons target the thickness editor");
        PositiveNumberInput.Increase.Execute(null, editor);
        Check(editor.Text == "13", "Up arrow increases thickness by 1 mm");
        PositiveNumberInput.Decrease.Execute(null, editor);
        Check(editor.Text == "12", "Down arrow decreases thickness by 1 mm");
        editor.Text = "1";
        Check(!PositiveNumberInput.Decrease.CanExecute(null, editor), "Down arrow cannot reach zero");
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "en-US", "it-IT" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
                editor.Text = $"0{separator}5";
                Check(!PositiveNumberInput.Decrease.CanExecute(null, editor), "Down arrow cannot produce negative fractions");
                PositiveNumberInput.Increase.Execute(null, editor);
                Check(editor.Text == $"1{separator}5", $"Spinner preserves decimals in {culture}");
                editor.SelectAll();
                foreach (var (input, rejected) in new[] { ("abc", true), ("-2", true), ($"1{separator}2{separator}3", true), ($"0{separator}5", false) })
                {
                    var args = new TextCompositionEventArgs(Keyboard.PrimaryDevice,
                        new TextComposition(InputManager.Current, editor, input))
                    {
                        RoutedEvent = TextCompositionManager.PreviewTextInputEvent
                    };
                    typeof(PositiveNumberInput).GetMethod("OnPreviewTextInput", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(editor, [args]);
                    Check(args.Handled == rejected, $"Typing '{input}' is {(rejected ? "rejected" : "accepted")} in {culture}");
                }
                foreach (var (input, rejected) in new[] { ("abc", true), ("-2", true), ("0", true), ("NaN", true), ($"0{separator}5", false) })
                {
                    var args = new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText, input), false, DataFormats.UnicodeText);
                    editor.RaiseEvent(args);
                    Check(args.CommandCancelled == rejected, $"Pasting '{input}' is {(rejected ? "rejected" : "accepted")} in {culture}");
                }
            }
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
        editor.Text = "18";
    }

    private static void SetCell(DataGrid grid, object row, string property, string text)
    {
        var column = grid.Columns.Single(column => column is DataGridTextColumn textColumn
            ? ((Binding)textColumn.Binding).Path.Path == property : column.SortMemberPath == property);
        grid.SelectedItem = row;
        grid.ScrollIntoView(row, column);
        grid.CurrentCell = new DataGridCellInfo(row, column);
        grid.BeginEdit();
        grid.UpdateLayout();
        var content = column.GetCellContent(row);
        if (column is DataGridTemplateColumn && Find<ComboBox>(content) is { } combo)
        {
            Check(combo.IsEditable, "Material type dropdown allows entering a new type");
            Check(combo.VerticalContentAlignment == VerticalAlignment.Center && combo.HorizontalContentAlignment == HorizontalAlignment.Left,
                "Material type editor is centered vertically and left-aligned");
            combo.IsDropDownOpen = true;
            if (combo.Items.Contains(text))
                combo.SelectedItem = text;
            else
                combo.Text = text;
            combo.IsDropDownOpen = false;
            combo.GetBindingExpression(ComboBox.TextProperty)!.UpdateSource();
            grid.CommitEdit(DataGridEditingUnit.Cell, true);
            return;
        }
        var editor = content as TextBox ?? Find<TextBox>(content);
        if (editor is null)
            throw new InvalidOperationException($"No editor for {property}");
        Check(editor.VerticalContentAlignment == VerticalAlignment.Center
            && editor.TextAlignment == (IsNumericProperty(property) ? TextAlignment.Right : TextAlignment.Left),
            $"{grid.Name}.{property} editor is centered vertically and aligned by value type");
        editor.Text = text;
        editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
    }

    private static void SelectMaterial(DataGrid grid, object row, Guid id)
    {
        var column = grid.Columns.OfType<DataGridComboBoxColumn>().Single(column => column.SelectedValuePath == "Id");
        grid.SelectedItem = row;
        grid.ScrollIntoView(row, column);
        grid.CurrentCell = new DataGridCellInfo(row, column);
        grid.BeginEdit();
        grid.UpdateLayout();
        var content = column.GetCellContent(row);
        var editor = content as ComboBox ?? Find<ComboBox>(content);
        if (editor is null)
            throw new InvalidOperationException("No material dropdown editor");
        Check(editor.VerticalContentAlignment == VerticalAlignment.Center && editor.HorizontalContentAlignment == HorizontalAlignment.Left,
            "Material selector editor is centered vertically and left-aligned");
        editor.SelectedValue = id;
        editor.GetBindingExpression(ComboBox.SelectedValueProperty)!.UpdateSource();
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
    }

    private static T? Find<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null)
            return null;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T matched)
                return matched;
            var nested = Find<T>(child);
            if (nested is not null)
                return nested;
        }
        return null;
    }

    private static void Check(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
        Console.WriteLine("PASS " + description);
    }
}