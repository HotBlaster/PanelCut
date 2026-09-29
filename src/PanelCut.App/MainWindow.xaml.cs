using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using PanelCut.App.Controls;
using PanelCut.App.Presentation;
using PanelCut.Core.Models;
using PanelCut.Core.Optimization;
using PanelCut.Core.Persistence;

namespace PanelCut.App;

public partial class MainWindow : Window
{
    private readonly WorkspaceViewModel workspace;
    private bool updating;
    private bool busy;
    private bool allowClose;
    private bool closing;
    private bool stockPending;
    private Task stockSave = Task.CompletedTask;
    private Task projectSave = Task.CompletedTask;
    private Inventory? pendingStockCandidate;
    private OptimizationResult? result;
    private bool materialPending;
    private Task materialSave = Task.CompletedTask;
    private MaterialCatalogue? pendingMaterialCandidate;
    private bool bladePending;
    private Task bladeSave = Task.CompletedTask;
    private BladeCatalogue? pendingBladeCandidate;
    private Guid panelMaterialId;
    private Guid scrapMaterialId;
    private bool changingMaterialFilter;

    public MainWindow() : this(null) { }

    public MainWindow(string? inventoryPath, string? materialsPath = null, string? bladesPath = null)
    {
        workspace = new WorkspaceViewModel(inventoryPath, materialsPath, bladesPath);
        updating = true;
        InitializeComponent();
        ConfigureGrids();
        UnitInput.ItemsSource = new[] { "mm", "inch" };
        UnitInput.SelectedIndex = 0;
        updating = false;
        InventoryLocation.Text = workspace.InventoryPath;
        InventoryLocation.ToolTip = workspace.InventoryPath;
        ScrapsLocation.Text = workspace.InventoryPath;
        ScrapsLocation.ToolTip = workspace.InventoryPath;
        PartsGrid.ItemsSource = workspace.Parts;
        PanelsGrid.ItemsSource = workspace.Panels;
        ScrapsGrid.ItemsSource = workspace.Scraps;
        PanelsGrid.Items.Filter = item => panelMaterialId == Guid.Empty || ((StockRow)item).MaterialId == panelMaterialId;
        ScrapsGrid.Items.Filter = item => scrapMaterialId == Guid.Empty || ((StockRow)item).MaterialId == scrapMaterialId;
        RefreshMaterialTabs();
        MaterialsGrid.DataContext = workspace;
        MaterialsGrid.ItemsSource = workspace.Materials;
        MaterialsLocation.Text = workspace.MaterialsPath;
        MaterialsLocation.ToolTip = workspace.MaterialsPath;
        BladesGrid.DataContext = workspace;
        BladesGrid.ItemsSource = workspace.BladeRows;
        BrandsGrid.ItemsSource = workspace.BrandRows;
        BladesLocation.Text = workspace.BladesPath;
        BladesLocation.ToolTip = workspace.BladesPath;
        RefreshBladeSelector();
        PanelsGrid.BeginningEdit += StockBeginningEdit;
        ScrapsGrid.BeginningEdit += StockBeginningEdit;
        CommandBindings.Add(new CommandBinding(ApplicationCommands.New, async (_, _) => await NewProjectAsync()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, async (_, _) => await OpenProjectAsync()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, async (_, _) => await SaveProjectAsync(false)));
        RefreshState();
    }

    private void ConfigureGrids()
    {
        AddText(PartsGrid, "Label", "Label", 150);
        AddText(PartsGrid, "Width (mm)", "Width", 100, numeric: true);
        AddText(PartsGrid, "Height (mm)", "Height", 100, numeric: true);
        AddText(PartsGrid, "Qty", "Quantity", 60, numeric: true);
        AddMaterialColumn(PartsGrid);
        AddText(PartsGrid, "Thickness (mm)", "Thickness", 120, true, numeric: true);
        AddText(PartsGrid, "Material status", "MaterialStatus", 120, true);
        AddCheck(PartsGrid, "Top", "EdgeBandTop");
        AddCheck(PartsGrid, "Bottom", "EdgeBandBottom");
        AddCheck(PartsGrid, "Left", "EdgeBandLeft");
        AddCheck(PartsGrid, "Right", "EdgeBandRight");
        AddText(PartsGrid, "Group / room", "GroupTag", 150);
        foreach (var grid in new[] { PanelsGrid, ScrapsGrid })
        {
            AddText(grid, "Label", "Label", 150);
            AddMaterialColumn(grid);
            AddText(grid, "Width mm", "Width", 95, numeric: true);
            AddText(grid, "Height mm", "Height", 95, numeric: true);
            AddText(grid, "Thickness mm", "Thickness", 110, true, numeric: true);
            AddText(grid, "Qty", "Quantity", 60, numeric: true);
            AddText(grid, "Priority", "Priority", 75, numeric: true);
            AddText(grid, "Cost / unit", "CostPerUnit", 100, numeric: true);
            AddText(grid, "Trim mm", "EdgeTrim", 85, numeric: true);
            AddText(grid, "Status", "Usability", 110, true);
        }
        AddText(ScrapsGrid, "Origin panel ID", "OriginPanelId", 280);
        AddText(BrandsGrid, "Name", "Name", 220);
        AddText(UnplacedGrid, "Label", "Part.Label", 180, true);
        AddText(UnplacedGrid, "Material", "Part.Material", 130, true);
        AddText(UnplacedGrid, "Remaining", "Quantity", 100, true, numeric: true);
        AddText(UnplacedGrid, "Type", "Part.MaterialType", 130, true);
        AddText(UnplacedGrid, "Thickness mm", "Part.Thickness", 110, true, numeric: true);
    }

    private void MaterialTypeDropDownClosed(object sender, EventArgs args) =>
        ((ComboBox)sender).GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();

    private void AddMaterialColumn(DataGrid grid) => grid.Columns.Add(new DataGridComboBoxColumn
    {
        Header = "Material", Width = 270, ItemsSource = workspace.MaterialOptions,
        DisplayMemberPath = "Display", SelectedValuePath = "Id",
        ElementStyle = (Style)FindResource("CellMaterialStyle"),
        EditingElementStyle = (Style)FindResource("CellMaterialEditorStyle"),
        SelectedValueBinding = new Binding("MaterialId") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
    });

    private void AddText(DataGrid grid, string header, string path, double width, bool readOnly = false, bool numeric = false) =>
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = header, Width = width, IsReadOnly = readOnly,
            ElementStyle = (Style)FindResource(numeric ? "CellNumberStyle" : "CellTextStyle"),
            EditingElementStyle = (Style)FindResource(numeric ? "CellNumberEditorStyle" : "CellTextEditorStyle"),
            Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay }
        });

    private void AddCheck(DataGrid grid, string header, string path) => grid.Columns.Add(new DataGridCheckBoxColumn
    {
        Header = header, Width = header.Length > 6 ? 100 : 65,
        ElementStyle = (Style)FindResource("CellCheckStyle"),
        EditingElementStyle = (Style)FindResource("CellCheckEditorStyle"),
        Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
    });

    private async void WindowLoaded(object sender, RoutedEventArgs args)
    {
        await LoadMaterialsAsync();
        await LoadBladesAsync();
        await LoadStockAsync();
        if (!workspace.MaterialsReady)
            ShowStatus("Materials could not be loaded. Reload the catalogue in Materials before editing or optimizing.", true);
        else if (!workspace.BladesReady)
            ShowStatus("Blades could not be loaded. Reload blades in Blades before editing or optimizing.", true);
    }

    private async Task LoadMaterialsAsync()
    {
        SetBusy(true, "Loading materials...");
        ClearLayout();
        try
        {
            await workspace.LoadMaterialsAsync();
            RefreshMaterialTabs();
            materialPending = false;
            pendingMaterialCandidate = null;
            ShowStatus("Materials loaded.");
        }
        catch (Exception exception) { ShowError(exception); }
        finally { SetBusy(false); }
    }

    private void MaterialBeginningEdit(object? sender, DataGridBeginningEditEventArgs args)
    {
        if (busy || !projectSave.IsCompleted || !materialSave.IsCompleted || stockPending || bladePending || !CommitProject())
        {
            args.Cancel = true;
            ShowStatus("Save or cancel the current stock/blade/project edit before editing materials.", true);
            return;
        }
        materialPending = true;
        pendingMaterialCandidate = null;
    }

    private void MaterialRowEnding(object sender, DataGridRowEditEndingEventArgs args)
    {
        if (updating)
            return;
        if (args.EditAction == DataGridEditAction.Cancel)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                workspace.RestoreMaterialRows();
                materialPending = false;
                pendingMaterialCandidate = null;
                RefreshState();
            });
            return;
        }
        if (!materialSave.IsCompleted)
        {
            args.Cancel = true;
            return;
        }
        try
        {
            args.Row.BindingGroup?.UpdateSources();
            materialSave = PersistMaterialsAsync(workspace.MaterialCandidate());
        }
        catch (Exception exception)
        {
            args.Cancel = true;
            materialPending = true;
            ShowError(exception);
        }
    }

    private async Task PersistMaterialsAsync(MaterialCatalogue candidate)
    {
        materialPending = true;
        pendingMaterialCandidate = candidate;
        await Dispatcher.Yield(DispatcherPriority.Background);
        SetBusy(true, "Saving materials...");
        try
        {
            await workspace.CommitManualMaterialEditAsync(candidate);
            RefreshMaterialTabs();
            materialPending = false;
            pendingMaterialCandidate = null;
            ClearLayout();
            ShowStatus("Materials saved. Referenced items updated; inventory and project files unchanged.");
        }
        catch (Exception exception) { ShowStatus($"Materials NOT saved: {exception.Message} Save to retry, or Cancel edit.", true); }
        finally { SetBusy(false); }
    }

    private async void AddMaterialClick(object sender, RoutedEventArgs args)
    {
        if (!await ReadyAsync() || !workspace.MaterialsReady || !CommitProject())
            return;
        var row = new MaterialRow();
        workspace.Materials.Add(row);
        materialPending = true;
        BeginRow(MaterialsGrid, row);
    }

    private async void RetryMaterialClick(object sender, RoutedEventArgs args)
    {
        if (!MaterialsGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !MaterialsGrid.CommitEdit(DataGridEditingUnit.Row, true))
            return;
        await materialSave;
        if (!workspace.MaterialsReady || busy)
            return;
        try
        {
            materialSave = PersistMaterialsAsync(pendingMaterialCandidate ?? workspace.MaterialCandidate());
            await materialSave;
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private void CancelMaterialClick(object sender, RoutedEventArgs args)
    {
        if (!materialSave.IsCompleted)
            return;
        CancelGrid(MaterialsGrid);
        workspace.RestoreMaterialRows();
        materialPending = false;
        pendingMaterialCandidate = null;
        RefreshState();
        ShowStatus("Material edit cancelled; catalogue unchanged.");
    }

    private async void ReloadMaterialsClick(object sender, RoutedEventArgs args)
    {
        if (await ReadyAsync() && CommitProject())
            await LoadMaterialsAsync();
    }

    private async Task LoadBladesAsync()
    {
        SetBusy(true, "Loading blades...");
        try
        {
            await workspace.LoadBladesAsync();
            bladePending = false;
            pendingBladeCandidate = null;
            ClearLayout();
            ShowStatus("Blades loaded.");
        }
        catch (Exception exception) { ShowError(exception); }
        finally
        {
            RefreshBladeSelector();
            SetBusy(false);
        }
    }

    private void BladeBeginningEdit(object? sender, DataGridBeginningEditEventArgs args)
    {
        if (busy || !projectSave.IsCompleted || !bladeSave.IsCompleted || stockPending || materialPending || !CommitProject())
        {
            args.Cancel = true;
            ShowStatus("Save or cancel the current stock/material/project edit before editing blades.", true);
            return;
        }
        bladePending = true;
        pendingBladeCandidate = null;
    }

    private void BladeRowEnding(object sender, DataGridRowEditEndingEventArgs args)
    {
        if (updating)
            return;
        if (args.EditAction == DataGridEditAction.Cancel)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                workspace.RestoreBladeRows();
                bladePending = false;
                pendingBladeCandidate = null;
                RefreshState();
            });
            return;
        }
        if (!bladeSave.IsCompleted)
        {
            args.Cancel = true;
            return;
        }
        try
        {
            args.Row.BindingGroup?.UpdateSources();
            bladeSave = PersistBladesAsync(workspace.BladeCandidate());
        }
        catch (Exception exception)
        {
            args.Cancel = true;
            bladePending = true;
            ShowError(exception);
        }
    }

    private async Task PersistBladesAsync(BladeCatalogue candidate)
    {
        bladePending = true;
        pendingBladeCandidate = candidate;
        await Dispatcher.Yield(DispatcherPriority.Background);
        SetBusy(true, "Saving blades...");
        try
        {
            await workspace.CommitManualBladeEditAsync(candidate);
            bladePending = false;
            pendingBladeCandidate = null;
            ClearLayout();
            RefreshBladeSelector();
            ShowStatus("Blades saved. Project and inventory files unchanged.");
        }
        catch (Exception exception) { ShowStatus($"Blades NOT saved: {exception.Message} Save to retry, or Cancel edit.", true); }
        finally { SetBusy(false); }
    }

    private bool CommitBladeGrids()
    {
        foreach (var grid in new[] { BladesGrid, BrandsGrid })
            if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true))
                return false;
        return true;
    }

    private async void AddBladeClick(object sender, RoutedEventArgs args)
    {
        if (!await ReadyAsync() || !workspace.BladesReady || !CommitProject())
            return;
        var row = workspace.CreateBladeRow();
        workspace.BladeRows.Add(row);
        bladePending = true;
        BeginRow(BladesGrid, row);
    }

    private async void AddBrandClick(object sender, RoutedEventArgs args)
    {
        if (!await ReadyAsync() || !workspace.BladesReady || !CommitProject())
            return;
        var row = new BrandRow();
        workspace.BrandRows.Add(row);
        bladePending = true;
        BeginRow(BrandsGrid, row);
    }

    private async void DeleteBladesClick(object sender, RoutedEventArgs args)
    {
        var ids = BladesGrid.SelectedItems.OfType<BladeRow>().Select(row => row.Id).ToArray();
        if (!await ReadyAsync() || !workspace.BladesReady || ids.Length == 0)
            return;
        var warning = workspace.Project.BladeId is { } used && ids.Contains(used) ? "\n\nThe current project uses one of them and will need another blade." : "";
        if (MessageBox.Show(this, $"Delete {ids.Length} selected blade(s)?{warning}", "PanelCut", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        await DeleteBladesAsync(ids);
    }

    private async Task DeleteBladesAsync(IReadOnlyCollection<Guid> ids)
    {
        CancelGrid(BladesGrid);
        try
        {
            var candidate = workspace.BladeCandidate();
            candidate.Blades.RemoveAll(blade => ids.Contains(blade.Id));
            bladeSave = PersistBladesAsync(candidate);
            await bladeSave;
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async void DeleteBrandsClick(object sender, RoutedEventArgs args)
    {
        var ids = BrandsGrid.SelectedItems.OfType<BrandRow>().Select(row => row.Id).ToArray();
        if (!await ReadyAsync() || !workspace.BladesReady || ids.Length == 0)
            return;
        if (MessageBox.Show(this, $"Delete {ids.Length} selected brand(s)?", "PanelCut", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        await DeleteBrandsAsync(ids);
    }

    private async Task DeleteBrandsAsync(IReadOnlyCollection<Guid> ids)
    {
        CancelGrid(BrandsGrid);
        try
        {
            bladeSave = PersistBladesAsync(workspace.BrandDeletionCandidate(ids));
            await bladeSave;
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async void RetryBladeClick(object sender, RoutedEventArgs args)
    {
        if (!CommitBladeGrids())
            return;
        await bladeSave;
        if (!workspace.BladesReady || busy)
            return;
        try
        {
            bladeSave = PersistBladesAsync(pendingBladeCandidate ?? workspace.BladeCandidate());
            await bladeSave;
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private void CancelBladeClick(object sender, RoutedEventArgs args)
    {
        if (!bladeSave.IsCompleted)
            return;
        CancelGrid(BladesGrid);
        CancelGrid(BrandsGrid);
        workspace.RestoreBladeRows();
        bladePending = false;
        pendingBladeCandidate = null;
        RefreshState();
        ShowStatus("Blade edit cancelled; blades unchanged.");
    }

    private async void ReloadBladesClick(object sender, RoutedEventArgs args)
    {
        if (await ReadyAsync() && CommitProject())
            await LoadBladesAsync();
    }

    private async Task LoadStockAsync()
    {
        SetBusy(true, "Loading inventory...");
        try
        {
            await workspace.LoadInventoryAsync();
            stockPending = false;
            ClearLayout();
            ShowStatus("Inventory loaded.");
        }
        catch (Exception exception) { ShowError(exception); }
        finally { SetBusy(false); }
    }

    private Guid? SelectedBladeId() => BladeInput.SelectedValue is Guid id ? id : workspace.Project.BladeId;

    private string UnitLabel => workspace.Project.Unit == LengthUnit.Millimetres ? "mm" : "inch";

    private void RefreshBladeSelector()
    {
        var wasUpdating = updating;
        updating = true;
        BladeInput.ItemsSource = workspace.ProjectBladeOptions();
        BladeInput.SelectedValue = workspace.Project.BladeId;
        updating = wasUpdating;
        KerfDisplay.Text = workspace.ProjectBlade is { } blade
            ? $"Kerf {EditableRow.Format(UnitConversion.FromMillimetres(blade.Kerf, workspace.Project.Unit))} {UnitLabel}"
            : workspace.Project.BladeId is null ? "Kerf: select a blade" : "Kerf: blade missing";
    }

    private void BladeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (updating)
            return;
        CommitProject();
        RefreshBladeSelector();
    }

    private bool CommitProject()
    {
        if (!projectSave.IsCompleted)
            return false;
        try
        {
            if (!PartsGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !PartsGrid.CommitEdit(DataGridEditingUnit.Row, true))
                return false;
            if (CommitProjectChanges())
                ClearLayout();
            RefreshState();
            return true;
        }
        catch (Exception exception) { ShowError(exception); return false; }
    }

    private bool CommitProjectChanges()
    {
        var existingIds = workspace.Project.Parts.Select(part => part.Id).ToHashSet();
        var hasNewParts = workspace.Parts.Any(part => !existingIds.Contains(part.Id));
        var changed = workspace.CommitProject(SelectedBladeId(), workspace.Project.Unit);
        if (changed && hasNewParts && workspace.ProjectPath is { } path)
            projectSave = PersistNewPartsAsync(path);
        return changed;
    }

    private async Task PersistNewPartsAsync(string path)
    {
        await Dispatcher.Yield(DispatcherPriority.Background);
        SetBusy(true, "Saving project...");
        try
        {
            await workspace.SaveProjectAsync(path);
            ShowStatus("Project saved.");
        }
        catch (Exception exception)
        {
            ShowStatus($"Project NOT saved: {exception.Message} Use File > Save to retry.", true);
        }
        finally { SetBusy(false); }
    }

    private void PartRowEnding(object sender, DataGridRowEditEndingEventArgs args)
    {
        if (updating)
            return;
        if (args.EditAction == DataGridEditAction.Cancel)
        {
            var cancelled = (PartRow)args.Row.Item;
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (!workspace.Project.Parts.Any(part => part.Id == cancelled.Id))
                    workspace.Parts.Remove(cancelled);
                RefreshState();
            });
            return;
        }
        if (!projectSave.IsCompleted)
        {
            args.Cancel = true;
            return;
        }
        try
        {
            args.Row.BindingGroup?.UpdateSources();
            ((PartRow)args.Row.Item).ValidateMaterial();
            ((PartRow)args.Row.Item).ToModel();
            if (CommitProjectChanges())
                ClearLayout();
            args.Row.ClearValue(ToolTipProperty);
            RefreshState();
        }
        catch (Exception exception)
        {
            args.Cancel = true;
            args.Row.ToolTip = exception.Message;
            ShowError(exception);
        }
    }

    private void StockBeginningEdit(object? sender, DataGridBeginningEditEventArgs args)
    {
        if (!stockSave.IsCompleted)
            args.Cancel = true;
        else
        {
            stockPending = true;
            pendingStockCandidate = null;
        }
    }

    private void StockRowEnding(object sender, DataGridRowEditEndingEventArgs args)
    {
        if (updating)
            return;
        if (args.EditAction == DataGridEditAction.Cancel)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                workspace.RestoreInventoryRows();
                stockPending = false;
                pendingStockCandidate = null;
                RefreshState();
            });
            return;
        }
        if (!stockSave.IsCompleted)
        {
            args.Cancel = true;
            return;
        }
        try
        {
            args.Row.BindingGroup?.UpdateSources();
            ((StockRow)args.Row.Item).ValidateMaterial();
            var candidate = workspace.InventoryCandidate();
            stockPending = true;
            stockSave = PersistStockAsync(candidate);
        }
        catch (Exception exception)
        {
            args.Cancel = true;
            stockPending = true;
            args.Row.ToolTip = exception.Message;
            ShowError(exception);
        }
    }

    private async Task PersistStockAsync(Inventory candidate)
    {
        pendingStockCandidate = candidate;
        await Dispatcher.Yield(DispatcherPriority.Background);
        SetBusy(true, "Saving inventory...");
        try
        {
            var changed = await workspace.CommitManualInventoryEditAsync(candidate);
            stockPending = false;
            pendingStockCandidate = null;
            if (changed)
                ClearLayout();
            CollectionViewSource.GetDefaultView(workspace.Panels).Refresh();
            CollectionViewSource.GetDefaultView(workspace.Scraps).Refresh();
            ShowStatus("Inventory saved.");
        }
        catch (Exception exception)
        {
            stockPending = true;
            ShowStatus($"Inventory NOT saved: {exception.Message} Save to retry, or Cancel edit.", true);
        }
        finally { SetBusy(false); }
    }

    private bool CommitStockGrid()
    {
        foreach (var grid in new[] { PanelsGrid, ScrapsGrid })
            if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true))
                return false;
        return true;
    }

    private async Task<bool> ReadyAsync()
    {
        if (!projectSave.IsCompleted)
            await projectSave;
        if (!materialSave.IsCompleted)
            await materialSave;
        if (!stockSave.IsCompleted)
            await stockSave;
        if (!bladeSave.IsCompleted)
            await bladeSave;
        if (busy)
            return false;
        if (materialPending)
        {
            ShowStatus("Materials have an unsaved edit. Save or cancel it in Materials.", true);
            return false;
        }
        if (bladePending)
        {
            ShowStatus("Blades have an unsaved edit. Save or cancel it in Blades.", true);
            return false;
        }
        if (stockPending)
        {
            ShowStatus("Stock has an unsaved edit. Save or cancel it in Panels or Scraps.", true);
            return false;
        }
        return true;
    }

    private void AddPartClick(object sender, RoutedEventArgs args)
    {
        if (busy || !projectSave.IsCompleted || materialPending || !workspace.MaterialsReady || !CommitProject())
            return;
        if (!projectSave.IsCompleted)
            return;
        var row = workspace.CreatePartRow();
        workspace.Parts.Add(row);
        BeginRow(PartsGrid, row);
        RefreshState();
    }
    private void DeletePartClick(object sender, RoutedEventArgs args)
    {
        var selected = PartsGrid.SelectedItems.OfType<PartRow>().ToArray();
        if (busy || !projectSave.IsCompleted || selected.Length == 0)
            return;
        CancelGrid(PartsGrid);
        foreach (var row in selected)
            workspace.Parts.Remove(row);
        CommitProject();
    }
    private async void ImportPartsClick(object sender, RoutedEventArgs args) => await ImportPartsAsync();
    private async Task ImportPartsAsync()
    {
        if (!await ReadyAsync() || !workspace.MaterialsReady || !CommitProject())
            return;
        await projectSave;
        var dialog = new OpenFileDialog { Filter = "CSV cut list (*.csv)|*.csv|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true)
            return;
        CutListImport import;
        SetBusy(true, "Importing cut list...");
        try { import = await workspace.ImportPartsAsync(dialog.FileName); }
        catch (Exception exception) { ShowError(exception); return; }
        finally { SetBusy(false); }
        Views.SelectedIndex = 0;
        if (import.Parts.Count > 0 && !CommitProject())
            return;
        await projectSave;
        var summary = $"Imported {import.Parts.Count} part row(s)" + (import.Skipped.Count > 0 ? $", skipped {import.Skipped.Count} invalid row(s)." : ".");
        // A failed autosave leaves the project dirty and its error in the status bar.
        if (workspace.ProjectPath is null || !workspace.IsDirty)
            ShowStatus(summary, import.Skipped.Count > 0);
        if (import.Skipped.Count > 0)
        {
            const int shown = 15;
            var lines = import.Skipped.Take(shown).Select(issue => $"Line {issue.Line}: {issue.Message}");
            var more = import.Skipped.Count > shown ? $"\n...and {import.Skipped.Count - shown} more." : "";
            MessageBox.Show(this, $"{summary}\n\n{string.Join("\n", lines)}{more}", "PanelCut - Import cut list", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private void CancelPartClick(object sender, RoutedEventArgs args)
    {
        if (busy || !projectSave.IsCompleted)
            return;
        CancelGrid(PartsGrid);
        workspace.RefreshPartRows();
        RefreshProjectControls();
        ShowStatus("Part edit cancelled.");
    }
    private async void AddPanelClick(object sender, RoutedEventArgs args) => await AddStockAsync(false);
    private async void AddScrapClick(object sender, RoutedEventArgs args) => await AddStockAsync(true);
    private async Task AddStockAsync(bool scrap)
    {
        if (!CommitStockGrid() || !await ReadyAsync() || !workspace.InventoryReady)
            return;
        Views.SelectedItem = scrap ? ScrapsTab : PanelsTab;
        var row = workspace.CreateStockRow(scrap);
        row.MaterialId = scrap ? scrapMaterialId : panelMaterialId;
        (scrap ? workspace.Scraps : workspace.Panels).Add(row);
        stockPending = true;
        BeginRow(scrap ? ScrapsGrid : PanelsGrid, row);
    }
    private async void DeleteStockClick(object sender, RoutedEventArgs args)
    {
        var grid = Views.SelectedItem == ScrapsTab ? ScrapsGrid : PanelsGrid;
        var selectedIds = grid.SelectedItems.OfType<StockRow>().Select(row => row.Id).ToArray();
        if (!await ReadyAsync() || !workspace.InventoryReady)
            return;
        if (selectedIds.Length == 0 || MessageBox.Show(this, $"Delete {selectedIds.Length} selected stock item(s)?", "PanelCut", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        CancelGrid(grid);
        await DeleteStockAsync(selectedIds);
    }

    private async Task DeleteStockAsync(IEnumerable<Guid> selectedIds)
    {
        var ids = selectedIds.ToHashSet();
        if (ids.Count == 0)
            return;
        var candidate = workspace.InventoryCandidate();
        candidate.Panels.RemoveAll(panel => ids.Contains(panel.Id));
        candidate.Scraps.RemoveAll(scrap => ids.Contains(scrap.Id));
        stockPending = true;
        stockSave = PersistStockAsync(candidate);
        await stockSave;
        if (!stockPending)
            workspace.RestoreInventoryRows();
    }
    private async void RetryStockClick(object sender, RoutedEventArgs args)
    {
        if (!CommitStockGrid())
            return;
        await stockSave;
        if (!workspace.InventoryReady || busy)
            return;
        try
        {
            stockSave = PersistStockAsync(pendingStockCandidate ?? workspace.InventoryCandidate());
            await stockSave;
            if (!stockPending)
                workspace.RestoreInventoryRows();
        }
        catch (Exception exception) { ShowError(exception); }
    }
    private void CancelStockClick(object sender, RoutedEventArgs args)
    {
        if (!stockSave.IsCompleted)
            return;
        CancelGrid(PanelsGrid);
        CancelGrid(ScrapsGrid);
        workspace.RestoreInventoryRows();
        stockPending = false;
        pendingStockCandidate = null;
        ShowStatus("Stock edit cancelled; saved inventory unchanged.");
    }
    private async void ReloadStockClick(object sender, RoutedEventArgs args)
    {
        if (await ReadyAsync())
            await LoadStockAsync();
    }
    private void CancelGrid(DataGrid grid)
    {
        updating = true;
        grid.CancelEdit(DataGridEditingUnit.Cell);
        grid.CancelEdit(DataGridEditingUnit.Row);
        updating = false;
    }
    private static void BeginRow(DataGrid grid, object row)
    {
        Window.GetWindow(grid)?.UpdateLayout();
        grid.SelectedItem = row;
        grid.ScrollIntoView(row);
        grid.UpdateLayout();
        grid.CurrentCell = new DataGridCellInfo(row, grid.Columns[0]);
        grid.Focus();
        grid.BeginEdit();
    }
    private void UnitChanged(object sender, SelectionChangedEventArgs args)
    {
        if (updating || UnitInput.SelectedIndex < 0)
            return;
        var next = UnitInput.SelectedIndex == 0 ? LengthUnit.Millimetres : LengthUnit.Inches;
        if (!CommitProject())
        {
            updating = true;
            UnitInput.SelectedIndex = workspace.Project.Unit == LengthUnit.Millimetres ? 0 : 1;
            updating = false;
            return;
        }
        if (workspace.CommitProject(workspace.Project.BladeId, next))
            ClearLayout();
        workspace.RefreshPartRows();
        RefreshProjectControls();
    }

    private async void NewProjectClick(object sender, RoutedEventArgs args) => await NewProjectAsync();
    private async void OpenProjectClick(object sender, RoutedEventArgs args) => await OpenProjectAsync();
    private async void SaveProjectClick(object sender, RoutedEventArgs args) => await SaveProjectAsync(false);
    private async void SaveAsClick(object sender, RoutedEventArgs args) => await SaveProjectAsync(true);
    private void ExitClick(object sender, RoutedEventArgs args) => Close();
    private void OpenInventoryFolderClick(object sender, RoutedEventArgs args) => OpenConfigurationFolder(workspace.InventoryPath);
    private void OpenMaterialsFolderClick(object sender, RoutedEventArgs args) => OpenConfigurationFolder(workspace.MaterialsPath);
    private void OpenBladesFolderClick(object sender, RoutedEventArgs args) => OpenConfigurationFolder(workspace.BladesPath);

    private void OpenConfigurationFolder(string filePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath)!;
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"Configuration folder does not exist: {directory}");
            using var process = Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task NewProjectAsync()
    {
        if (!await ReadyAsync() || !await ConfirmProjectAsync())
            return;
        CancelGrid(PartsGrid);
        workspace.NewProject();
        ClearLayout();
        RefreshProjectControls();
        Views.SelectedIndex = 0;
        ShowStatus("New project.");
    }
    private async Task OpenProjectAsync()
    {
        if (!await ReadyAsync())
            return;
        var dialog = new OpenFileDialog { Filter = "PanelCut project (*.panelcut.json)|*.panelcut.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true || !await ConfirmProjectAsync())
            return;
        SetBusy(true, "Opening project...");
        try
        {
            await workspace.OpenProjectAsync(dialog.FileName);
            CancelGrid(PartsGrid);
            ClearLayout();
            RefreshProjectControls();
            Views.SelectedIndex = 0;
            ShowStatus("Project opened.");
        }
        catch (Exception exception) { ShowError(exception); }
        finally { SetBusy(false); }
    }
    private async Task<bool> SaveProjectAsync(bool saveAs)
    {
        if (!await ReadyAsync() || !CommitProject())
            return false;
        await projectSave;
        var path = workspace.ProjectPath;
        if (saveAs || path is null)
        {
            var dialog = new SaveFileDialog { Filter = "PanelCut project (*.panelcut.json)|*.panelcut.json", DefaultExt = ".panelcut.json", AddExtension = true, FileName = path is null ? "Untitled.panelcut.json" : Path.GetFileName(path) };
            if (dialog.ShowDialog(this) != true)
                return false;
            path = dialog.FileName;
        }
        SetBusy(true, "Saving project...");
        try
        {
            await workspace.SaveProjectAsync(path);
            ShowStatus("Project saved.");
            return true;
        }
        catch (Exception exception) { ShowError(exception); return false; }
        finally { SetBusy(false); }
    }
    private bool ProjectHasDrafts()
    {
        try { return workspace.HasProjectDrafts(SelectedBladeId(), workspace.Project.Unit); }
        catch (ArgumentException) { return true; }
    }
    private async Task<bool> ConfirmProjectAsync()
    {
        if (!workspace.IsDirty && !ProjectHasDrafts())
            return true;
        var choice = MessageBox.Show(this, "Save changes to this project?", "PanelCut", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return choice == MessageBoxResult.No || (choice == MessageBoxResult.Yes && await SaveProjectAsync(false));
    }
    private async void WindowClosing(object? sender, CancelEventArgs args)
    {
        if (allowClose)
            return;
        args.Cancel = true;
        if (closing || busy)
            return;
        closing = true;
        try
        {
            if (!await ReadyAsync() || !await ConfirmProjectAsync())
                return;
            allowClose = true;
            _ = Dispatcher.BeginInvoke(Close);
        }
        finally { closing = false; }
    }

    private async void OptimizeClick(object sender, RoutedEventArgs args) => await OptimizeAsync();

    private async Task OptimizeAsync()
    {
        if (!await ReadyAsync() || !workspace.InventoryReady || !workspace.MaterialsReady || !workspace.BladesReady || !CommitProject())
            return;
        await projectSave;
        if (workspace.ProjectBlade is null)
        {
            Views.SelectedItem = ProjectTab;
            BladeInput.Focus();
            ShowStatus(workspace.Project.BladeId is null ? "Select a blade in Project before optimizing."
                : "The project's blade was deleted. Select another blade in Project before optimizing.", true);
            return;
        }
        SetBusy(true, "Optimizing...");
        ClearLayout();
        try
        {
            var inventory = workspace.InventoryCandidate();
            var project = workspace.Project;
            var blades = workspace.Blades;
            result = await Task.Run(() => new PanelOptimizer().OptimizePanels(inventory, project, workspace.Catalogue, blades));
            Metrics.Text = $"{result.StockItemsUsed} sheets   |   Waste {result.WastePercentage:F1}%   |   Cost {result.TotalCost:N2}   |   Unplaced {result.UnplacedParts.Sum(part => (long)part.Quantity)}   |   {result.Settings.BladeName}, kerf {EditableRow.Format(result.Settings.KerfWidth)} mm";
            SheetSelector.ItemsSource = result.Sheets.Select((sheet, index) => $"{index + 1} / {result.Sheets.Count}   {sheet.Stock.Label}   {sheet.Stock.Material} / {sheet.Stock.MaterialType} / {sheet.Stock.Thickness:G} mm   {sheet.Stock.Kind} #{sheet.UnitIndex}   {sheet.Stock.Width:G} x {sheet.Stock.Height:G} mm").ToArray();
            SheetSelector.SelectedIndex = result.Sheets.Count > 0 ? 0 : -1;
            UnplacedGrid.ItemsSource = result.UnplacedParts;
            UnplacedExpander.Visibility = result.IsComplete ? Visibility.Collapsed : Visibility.Visible;
            UnplacedExpander.Header = $"Unplaced parts ({result.UnplacedParts.Sum(part => (long)part.Quantity)})";
            UnplacedExpander.IsExpanded = false;
            Views.SelectedItem = LayoutTab;
            ShowStatus(result.IsComplete ? "Optimization complete. Inventory unchanged." : "Partial layout: some parts were not placed. Inventory unchanged.");
        }
        catch (Exception exception) { ClearLayout(); ShowError(exception); }
        finally { SetBusy(false); }
    }
    private void SheetChanged(object sender, SelectionChangedEventArgs args) => RenderSheet();
    private void PreviousSheetClick(object sender, RoutedEventArgs args)
    {
        if (SheetSelector.SelectedIndex > 0)
            SheetSelector.SelectedIndex--;
    }
    private void NextSheetClick(object sender, RoutedEventArgs args)
    {
        if (SheetSelector.SelectedIndex + 1 < SheetSelector.Items.Count)
            SheetSelector.SelectedIndex++;
    }
    private void RenderSheet()
    {
        DrawingHost.Child = result is not null && SheetSelector.SelectedIndex >= 0
            ? new SheetDrawing(result.Sheets[SheetSelector.SelectedIndex], result.Settings.Unit)
            : null;
    }
    private void ClearLayout()
    {
        result = null;
        Metrics.Text = "No layout";
        SheetSelector.ItemsSource = null;
        UnplacedGrid.ItemsSource = null;
        UnplacedExpander.Visibility = Visibility.Collapsed;
        DrawingHost.Child = null;
    }
    private void RefreshMaterialTabs()
    {
        changingMaterialFilter = true;
        try
        {
            var options = new[] { new MaterialOption(Guid.Empty, "All") }
                .Concat(workspace.Catalogue.Materials.Select(material => new MaterialOption(material.Id, material.Name))).ToArray();
            if (!options.Any(option => option.Id == panelMaterialId))
                panelMaterialId = Guid.Empty;
            if (!options.Any(option => option.Id == scrapMaterialId))
                scrapMaterialId = Guid.Empty;
            PanelMaterialTabs.ItemsSource = options;
            ScrapMaterialTabs.ItemsSource = options;
            PanelMaterialTabs.SelectedItem = options.Single(option => option.Id == panelMaterialId);
            ScrapMaterialTabs.SelectedItem = options.Single(option => option.Id == scrapMaterialId);
            UpdateMaterialColumnVisibility(PanelsGrid, panelMaterialId);
            UpdateMaterialColumnVisibility(ScrapsGrid, scrapMaterialId);
            CollectionViewSource.GetDefaultView(workspace.Panels).Refresh();
            CollectionViewSource.GetDefaultView(workspace.Scraps).Refresh();
        }
        finally { changingMaterialFilter = false; }
    }

    private async void StockMaterialChanged(object sender, SelectionChangedEventArgs args)
    {
        if (changingMaterialFilter || args.Source != sender || sender is not TabControl tabs
            || tabs.SelectedItem is not MaterialOption selected)
            return;
        var isScrap = tabs == ScrapMaterialTabs;
        var previousId = isScrap ? scrapMaterialId : panelMaterialId;
        if (selected.Id == previousId)
            return;
        changingMaterialFilter = true;
        tabs.SelectedItem = tabs.Items.Cast<MaterialOption>().Single(option => option.Id == previousId);
        changingMaterialFilter = false;
        if (!CommitStockGrid() || !await ReadyAsync())
            return;
        changingMaterialFilter = true;
        try
        {
            if (isScrap)
                scrapMaterialId = selected.Id;
            else
                panelMaterialId = selected.Id;
            tabs.SelectedItem = selected;
            var grid = isScrap ? ScrapsGrid : PanelsGrid;
            UpdateMaterialColumnVisibility(grid, selected.Id);
            grid.UnselectAll();
            CollectionViewSource.GetDefaultView(grid.ItemsSource).Refresh();
        }
        finally { changingMaterialFilter = false; }
    }

    private static void UpdateMaterialColumnVisibility(DataGrid grid, Guid materialId) =>
        grid.Columns.OfType<DataGridComboBoxColumn>().Single().Visibility =
            materialId == Guid.Empty ? Visibility.Visible : Visibility.Collapsed;

    private void ViewChanged(object sender, SelectionChangedEventArgs args)
    {
        if (args.Source == Views && !updating)
        {
            if (materialPending && Views.SelectedItem != MaterialsTab)
            {
                Views.SelectedItem = MaterialsTab;
                ShowStatus("Save or cancel the material edit before leaving Materials.", true);
            }
            else if (bladePending && Views.SelectedItem != BladesTab)
            {
                Views.SelectedItem = BladesTab;
                ShowStatus("Save or cancel the blade edit before leaving Blades.", true);
            }
            RefreshState();
        }
    }
    private void RefreshProjectControls()
    {
        updating = true;
        UnitInput.SelectedIndex = workspace.Project.Unit == LengthUnit.Millimetres ? 0 : 1;
        PartsGrid.Columns[1].Header = $"Width ({UnitLabel})";
        PartsGrid.Columns[2].Header = $"Height ({UnitLabel})";
        PartsGrid.Columns[5].Header = $"Thickness ({UnitLabel})";
        updating = false;
        RefreshBladeSelector();
        RefreshState();
    }
    private void RefreshState()
    {
        if (OptimizeButton is null || PartsGrid is null)
            return;
        var name = workspace.ProjectPath is null ? "Untitled project" : Path.GetFileName(workspace.ProjectPath);
        var marker = workspace.IsDirty ? " *" : "";
        Title = $"PanelCut - {name}{marker}";
        ProjectName.Text = name + marker;
        PartCount.Text = $"{workspace.Parts.Count} part rows";
        OptimizeButton.IsEnabled = !busy && workspace.InventoryReady && workspace.MaterialsReady && workspace.BladesReady && workspace.Parts.Count > 0;
        PartsGrid.IsEnabled = AddPartButton.IsEnabled = ImportPartsButton.IsEnabled = workspace.MaterialsReady;
        PanelsGrid.IsEnabled = ScrapsGrid.IsEnabled = workspace.InventoryReady && workspace.MaterialsReady;
        AddPanelButton.IsEnabled = AddScrapButton.IsEnabled = DeleteStockButton.IsEnabled = RetryEditButton.IsEnabled = workspace.InventoryReady && workspace.MaterialsReady;
        DeleteScrapsButton.IsEnabled = SaveScrapsButton.IsEnabled = workspace.InventoryReady && workspace.MaterialsReady;
        MaterialsGrid.IsEnabled = AddMaterialButton.IsEnabled = CommitMaterialButton.IsEnabled = workspace.MaterialsReady;
        BladesGrid.IsEnabled = BrandsGrid.IsEnabled = AddBladeButton.IsEnabled = AddBrandButton.IsEnabled = BladeInput.IsEnabled
            = DeleteBladesButton.IsEnabled = DeleteBrandsButton.IsEnabled = SaveBladesButton.IsEnabled = workspace.BladesReady;
    }
    private void SetBusy(bool value, string? message = null)
    {
        busy = value;
        Views.IsEnabled = FileMenu.IsEnabled = !value;
        Progress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        if (message is not null)
            ShowStatus(message);
        RefreshState();
    }
    private void ShowError(Exception exception) => ShowStatus(exception.Message, true);
    private void ShowStatus(string message, bool error = false)
    {
        Status.Text = message;
        Status.Foreground = new SolidColorBrush(error ? Color.FromRgb(150, 48, 35) : Color.FromRgb(34, 65, 58));
    }
}