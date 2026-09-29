using System.Security.Cryptography;
using System.Text;
using PanelCut.Core.Models;
using PanelCut.Core.Optimization;

namespace PanelCut.Core.Tests;

internal static class TestMaterials
{
    internal static Guid Id(string name, double thickness = 18) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{name}/{thickness}"))).AsSpan(0, 16));

    internal static MaterialCatalogue Catalogue()
    {
        var catalogue = new MaterialCatalogue();
        foreach (var name in new[] { "Oak", "oak", "Birch" })
        foreach (var thickness in new[] { 1d, 12d, 18d })
            catalogue.Materials.Add(new Material(name, "Plywood", thickness) { Id = Id(name, thickness) });
        return catalogue;
    }
}

internal sealed class TestOptimizer
{
    internal OptimizationResult OptimizePanels(Inventory inventory, Project project) =>
        new PanelOptimizer().OptimizePanels(inventory, project, TestMaterials.Catalogue());
}