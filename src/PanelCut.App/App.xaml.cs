using System.Windows;

namespace PanelCut.App;

public partial class App : Application
{
	protected override void OnStartup(StartupEventArgs args)
	{
		base.OnStartup(args);
		try
		{
			var paths = ParsePaths(args.Args);
			MainWindow = new MainWindow(paths.Inventory, paths.Materials);
			MainWindow.Show();
		}
		catch (Exception exception)
		{
			MessageBox.Show(exception.Message, "PanelCut startup error", MessageBoxButton.OK, MessageBoxImage.Error);
			Shutdown(1);
		}
	}

	internal static (string? Inventory, string? Materials) ParsePaths(string[] arguments)
	{
		var options = new Dictionary<string, string>(StringComparer.Ordinal);
		for (var index = 0; index < arguments.Length; index += 2)
		{
			if (index + 1 >= arguments.Length
				|| arguments[index] is not ("--inventory-path" or "--materials-path")
				|| string.IsNullOrWhiteSpace(arguments[index + 1])
				|| arguments[index + 1].StartsWith("--", StringComparison.Ordinal)
				|| !options.TryAdd(arguments[index], arguments[index + 1]))
				throw new ArgumentException("Usage: PanelCut.App [--inventory-path <inventory.json>] [--materials-path <materials.json>]");
		}
		return (options.GetValueOrDefault("--inventory-path"), options.GetValueOrDefault("--materials-path"));
	}
}
