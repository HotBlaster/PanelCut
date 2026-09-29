using PanelCut.Core.Models;

namespace PanelCut.Core.Persistence;

public sealed class InventoryStore
{
    private readonly JsonFileStore files = new();

    public InventoryStore(string? filePath = null)
    {
        FilePath = Path.GetFullPath(filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PanelCut", "inventory.json"));
    }

    public string FilePath { get; }

    public async Task<Inventory> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await files.LoadAsync<InventoryDocument, Inventory>(FilePath, document => document.ToModel(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (FileNotFoundException) { return new Inventory(); }
        catch (DirectoryNotFoundException) { return new Inventory(); }
    }

    public Task SaveAsync(Inventory inventory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        return files.SaveAsync(FilePath, new InventoryDocument(inventory), cancellationToken);
    }
}