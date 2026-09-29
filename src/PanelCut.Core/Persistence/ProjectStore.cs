using PanelCut.Core.Models;

namespace PanelCut.Core.Persistence;

public sealed class ProjectStore
{
    private readonly JsonFileStore files = new();

    public Task<Project> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return files.LoadAsync<ProjectDocument, Project>(Path.GetFullPath(path), document => document.ToModel(), cancellationToken);
    }

    public Task SaveAsync(string path, Project project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        return files.SaveAsync(Path.GetFullPath(path), new ProjectDocument(project), cancellationToken);
    }
}