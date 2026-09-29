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
        foreach (var name in new[] { "Oak", "Pine", "Birch" })
        foreach (var thickness in new[] { 1d, 12d, 18d })
            catalogue.Materials.Add(new Material(Name(name, thickness), "Plywood", thickness) { Id = Id(name, thickness) });
        return catalogue;
    }

    // Names must be unique, so only the 18 mm variant keeps the bare name.
    internal static string Name(string name, double thickness = 18) =>
        thickness == 18 ? name : FormattableString.Invariant($"{name} {thickness}");

    private static readonly double[] Kerfs = [0, 0.1, 0.5, 1, 2, 3.2, 8, 1000];

    internal static Guid BladeId(double kerf) => Array.IndexOf(Kerfs, kerf) >= 0 ? Id("blade", kerf)
        : throw new ArgumentOutOfRangeException(nameof(kerf), "Add the kerf to TestMaterials.Kerfs.");

    internal static BladeCatalogue Blades()
    {
        var blades = new BladeCatalogue();
        foreach (var kerf in Kerfs)
            blades.Blades.Add(new Blade(FormattableString.Invariant($"Kerf {kerf}"), 250, 48, kerf) { Id = BladeId(kerf) });
        return blades;
    }

    internal static Project Project(double kerf = 0) => new() { BladeId = BladeId(kerf) };
}

internal sealed class TestOptimizer
{
    internal OptimizationResult OptimizePanels(Inventory inventory, Project project) =>
        new PanelOptimizer().OptimizePanels(inventory, project, TestMaterials.Catalogue(), TestMaterials.Blades());
}