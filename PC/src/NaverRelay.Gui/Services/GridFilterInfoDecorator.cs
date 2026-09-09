namespace NaverRelay.Gui.Services;

/// <summary>
/// 선수명 바로 오른쪽에 현재 그리드 정렬 기준과 해당 행의 값을 표시합니다.
/// 예: wRC+ ↓ 149, OPS ↑ 0.930. 바인딩 DTO에는 영향을 주지 않는 화면 전용 열입니다.
/// </summary>
internal static class GridFilterInfoDecorator
{
    public const string ColumnName = "__AppliedRecordFilter";

    public static void Apply(DataGridView grid, string filterDescription, bool visible)
    {
        if (grid.Columns.Contains(ColumnName))
            grid.Columns.Remove(ColumnName);
        if (!visible) return;

        var nameColumn = grid.Columns
            .Cast<DataGridViewColumn>()
            .FirstOrDefault(column =>
                string.Equals(column.DataPropertyName, "Name", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(column.HeaderText, "Name", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(column.HeaderText, "선수", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(column.HeaderText, "투수", StringComparison.OrdinalIgnoreCase));
        if (nameColumn is null) return;

        var column = new DataGridViewTextBoxColumn
        {
            Name = ColumnName,
            HeaderText = "적용 조건",
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            Width = 230,
            MinimumWidth = 160,
            Tag = string.IsNullOrWhiteSpace(filterDescription) ? "전체" : filterDescription,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                ForeColor = Color.DimGray,
                Font = new Font(grid.Font, FontStyle.Italic),
                Alignment = DataGridViewContentAlignment.MiddleLeft,
            },
        };
        grid.Columns.Add(column);
        column.DisplayIndex = Math.Min(nameColumn.DisplayIndex + 1, grid.Columns.Count - 1);
    }

    public static bool TryFormat(DataGridView grid, DataGridViewCellFormattingEventArgs eventArgs)
    {
        if (eventArgs.ColumnIndex < 0 || eventArgs.ColumnIndex >= grid.Columns.Count) return false;
        var column = grid.Columns[eventArgs.ColumnIndex];
        if (!string.Equals(column.Name, ColumnName, StringComparison.Ordinal)) return false;

        var sorted = grid.SortedColumn;
        if (sorted is null || string.Equals(sorted.Name, ColumnName, StringComparison.Ordinal))
        {
            eventArgs.Value = "-";
            eventArgs.FormattingApplied = true;
            return true;
        }

        var row = eventArgs.RowIndex >= 0 && eventArgs.RowIndex < grid.Rows.Count ? grid.Rows[eventArgs.RowIndex] : null;
        if (row is null)
        {
            eventArgs.Value = "-";
            eventArgs.FormattingApplied = true;
            return true;
        }

        var value = row.Cells[sorted.Index].FormattedValue?.ToString();
        if (string.IsNullOrWhiteSpace(value)) value = "-";
        var arrow = grid.SortOrder == SortOrder.Ascending ? "↑" : "↓";
        eventArgs.Value = $"{sorted.HeaderText} {arrow} {value}";
        eventArgs.FormattingApplied = true;
        return true;
    }
}
