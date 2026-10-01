using System.Globalization;
using System.Text;
using PanelCut.Core.Models;

namespace PanelCut.Core.Persistence;

public sealed record CutListIssue(int Line, string Message);

public sealed record CutListImport(IReadOnlyList<Part> Parts, IReadOnlyList<CutListIssue> Skipped);

/// <summary>Reads a CSV cut list (comma or semicolon separated, header row, lengths in millimetres).</summary>
public static class CutListImporter
{
    private static readonly string[] Required = ["Width", "Height", "Material"];
    private static readonly string[] Supported =
        ["Label", "Width", "Height", "Quantity", "Material", "Type", "Thickness", "Color"];

    public static async Task<CutListImport> LoadAsync(string path, MaterialCatalogue catalogue,
        Func<string>? missingColor = null, CancellationToken cancellationToken = default) =>
        Parse(await File.ReadAllTextAsync(path, cancellationToken), catalogue, missingColor);

    /// <param name="missingColor">Supplies the colour of rows with an empty Color cell; defaults to <see cref="Part.DefaultColor"/>.</param>
    public static CutListImport Parse(string text, MaterialCatalogue catalogue, Func<string>? missingColor = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(catalogue);
        text = text.TrimStart('\uFEFF');
        var records = ReadRecords(text, DetectDelimiter(text))
            .Where(record => record.Fields.Any(field => !string.IsNullOrWhiteSpace(field))).ToList();
        if (records.Count == 0)
            throw new InvalidDataException("The cut list is empty. The first row must name the columns.");
        var columns = ReadHeader(records[0].Fields);
        var parts = new List<Part>();
        var skipped = new List<CutListIssue>();
        foreach (var (line, fields) in records.Skip(1))
        {
            try { parts.Add(ParseRow(fields, columns, records[0].Fields.Length, catalogue, missingColor)); }
            catch (FormatException exception) { skipped.Add(new CutListIssue(line, exception.Message)); }
        }
        return new CutListImport(parts, skipped);
    }

    private static Dictionary<string, int> ReadHeader(string[] header)
    {
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < header.Length; index++)
        {
            var name = header[index].Trim();
            if (!Supported.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unknown column '{name}'. Supported columns: {string.Join(", ", Supported)}.");
            if (!columns.TryAdd(name, index))
                throw new InvalidDataException($"Column '{name}' appears more than once.");
        }
        var missing = Required.Where(name => !columns.ContainsKey(name)).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException($"Missing required column(s): {string.Join(", ", missing)}.");
        return columns;
    }

    private static Part ParseRow(string[] fields, Dictionary<string, int> columns, int columnCount, MaterialCatalogue catalogue,
        Func<string>? missingColor)
    {
        if (fields.Length > columnCount)
            throw new FormatException($"Row has {fields.Length} values but the header has {columnCount} columns.");
        string Value(string column) => columns.TryGetValue(column, out var index) && index < fields.Length ? fields[index].Trim() : "";
        var part = new Part(Number(Value("Width"), "Width"), Number(Value("Height"), "Height"),
            ResolveMaterial(Value("Material"), Value("Type"), Value("Thickness"), catalogue), Quantity(Value("Quantity")))
        {
            Label = Value("Label"),
        };
        var color = Value("Color");
        // Assigned last so the provider is only consumed by rows that parsed successfully.
        part.Color = color.Length == 0 ? missingColor?.Invoke() ?? Part.DefaultColor : Color(color);
        return part;
    }

    private static Guid ResolveMaterial(string name, string type, string thickness, MaterialCatalogue catalogue)
    {
        if (name.Length == 0)
            throw new FormatException("Material is required.");
        var material = catalogue.Materials.FirstOrDefault(material => string.Equals(material.Name.Trim(), name, StringComparison.OrdinalIgnoreCase))
            ?? throw new FormatException($"Material '{name}' is not in the catalogue.");
        if (type.Length > 0 && !string.Equals(material.Type.Trim(), type, StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"Material '{name}' has type '{material.Type}', not '{type}'.");
        if (thickness.Length > 0 && material.Thickness != Number(thickness, "Thickness"))
            throw new FormatException($"Material '{name}' is {material.Thickness.ToString(CultureInfo.InvariantCulture)} mm thick, not {thickness} mm.");
        return material.Id;
    }

    private static string Color(string value)
    {
        try { return Validation.Color(value); }
        catch (ArgumentException) { throw new FormatException($"Color must be in #RRGGBB format (found '{value}')."); }
    }

    private static double Number(string value, string column)
    {
        var normalized = value.Contains('.') ? value : value.Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            || !double.IsFinite(number) || number <= 0)
            throw new FormatException($"{column} must be a positive number of millimetres (found '{value}').");
        return number;
    }

    private static int Quantity(string value)
    {
        if (value.Length == 0)
            return 1;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity < 1)
            throw new FormatException($"Quantity must be a whole number of at least 1 (found '{value}').");
        return quantity;
    }

    private static char DetectDelimiter(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        var header = end < 0 ? text : text[..end];
        return header.Count(c => c == ';') > header.Count(c => c == ',') ? ';' : ',';
    }

    private static IEnumerable<(int Line, string[] Fields)> ReadRecords(string text, char delimiter)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var line = 1;
        var start = 1;
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            if (quoted)
            {
                if (c == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (c == '"')
                    quoted = false;
                else
                {
                    if (c == '\n')
                        line++;
                    field.Append(c);
                }
            }
            else if (c == '"' && field.Length == 0)
                quoted = true;
            else if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                    index++;
                fields.Add(field.ToString());
                field.Clear();
                yield return (start, fields.ToArray());
                fields.Clear();
                start = ++line;
            }
            else
                field.Append(c);
        }
        if (quoted)
            throw new InvalidDataException($"Line {start}: a quoted value is not closed.");
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return (start, fields.ToArray());
        }
    }
}
