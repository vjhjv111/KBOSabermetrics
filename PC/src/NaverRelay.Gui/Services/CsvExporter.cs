using System.Globalization;
using System.Text;
using NaverRelay.Application.Statistics;

namespace NaverRelay.Gui.Services;

internal static class CsvExporter
{
    public static async Task ExportAsync(
        DataGridView grid,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var visibleColumns = grid.Columns
            .Cast<DataGridViewColumn>()
            .Where(column => column.Visible)
            .OrderBy(column => column.DisplayIndex)
            .ToList();

        if (visibleColumns.Count == 0)
        {
            throw new InvalidOperationException("내보낼 열이 없습니다.");
        }

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", visibleColumns.Select(column => Escape(column.HeaderText))));

        foreach (DataGridViewRow row in grid.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.IsNewRow)
            {
                continue;
            }

            var values = visibleColumns.Select(column =>
            {
                var value = row.Cells[column.Index].Value;
                var property = string.IsNullOrWhiteSpace(column.DataPropertyName) ? column.Name : column.DataPropertyName;
                return Escape(FormatValue(value, PrecisionFormat(property, row.DataBoundItem)));
            });
            builder.AppendLine(string.Join(",", values));
        }

        await File.WriteAllTextAsync(
            filePath,
            builder.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            cancellationToken);
    }


    public static async Task ExportRowsAsync<T>(
        IEnumerable<T> rows,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var properties = System.ComponentModel.TypeDescriptor.GetProperties(typeof(T))
            .Cast<System.ComponentModel.PropertyDescriptor>()
            .Where(property => property.IsBrowsable)
            .OrderBy(property => GridNumberFormatter.IsWarValue(property.Name))
            .ToList();

        if (properties.Count == 0)
            throw new InvalidOperationException("내보낼 열이 없습니다.");

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", properties.Select(property => Escape(property.DisplayName))));

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.AppendLine(string.Join(",", properties.Select(property =>
                Escape(FormatValue(property.GetValue(row), PrecisionFormat(property.Name, row))))));
        }

        await File.WriteAllTextAsync(
            filePath,
            builder.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            cancellationToken);
    }

    private static string? PrecisionFormat(string property, object? row) =>
        property == nameof(LeagueConstantGridRow.Value) && row is LeagueConstantGridRow constant
            ? GridNumberFormatter.GetLeagueConstantPrecisionFormat(constant.Metric ?? string.Empty)
            : GridNumberFormatter.GetMetricPrecisionFormat(property);

    private static string FormatValue(object? value, string? format = null)
    {
        return value switch
        {
            null => string.Empty,
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(format, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Contains('"'))
        {
            text = text.Replace("\"", "\"\"");
        }

        return text.IndexOfAny([',', '\r', '\n', '"']) >= 0 ? $"\"{text}\"" : text;
    }
}
