using System.Collections;
using System.ComponentModel;

namespace NaverRelay.Gui.Services;

/// <summary>
/// DataGridView 자동 열 정렬을 지원하는 BindingList입니다.
/// 열 머리글을 누를 때 오름차순/내림차순이 번갈아 적용됩니다.
/// </summary>
public sealed class SortableBindingList<T> : BindingList<T>
{
    private bool _isSorted;
    private ListSortDirection _sortDirection;
    private PropertyDescriptor? _sortProperty;

    public SortableBindingList(IEnumerable<T> items)
        : base(items.ToList())
    {
    }

    protected override bool SupportsSortingCore => true;
    protected override bool IsSortedCore => _isSorted;
    protected override ListSortDirection SortDirectionCore => _sortDirection;
    protected override PropertyDescriptor? SortPropertyCore => _sortProperty;

    protected override void ApplySortCore(
        PropertyDescriptor property,
        ListSortDirection direction)
    {
        if (Items is not List<T> list)
        {
            return;
        }

        list.Sort(new PropertyComparer(property, direction));
        _sortProperty = property;
        _sortDirection = direction;
        _isSorted = true;
        ResetBindings();
    }

    protected override void RemoveSortCore()
    {
        _isSorted = false;
        _sortProperty = null;
    }

    private sealed class PropertyComparer : IComparer<T>
    {
        private readonly PropertyDescriptor _property;
        private readonly int _direction;

        public PropertyComparer(
            PropertyDescriptor property,
            ListSortDirection direction)
        {
            _property = property;
            _direction = direction == ListSortDirection.Ascending ? 1 : -1;
        }

        public int Compare(T? left, T? right)
        {
            var leftValue = left == null ? null : _property.GetValue(left);
            var rightValue = right == null ? null : _property.GetValue(right);
            return CompareValues(leftValue, rightValue) * _direction;
        }

        private static int CompareValues(object? left, object? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return 1;   // 값 없음은 항상 아래쪽에 표시
            if (right is null) return -1;

            if (left is string leftText && right is string rightText)
            {
                return StringComparer.CurrentCultureIgnoreCase.Compare(
                    leftText,
                    rightText);
            }

            if (left is IComparable comparable)
            {
                try
                {
                    return comparable.CompareTo(right);
                }
                catch (ArgumentException)
                {
                    // 서로 다른 숫자 형식 등은 아래 문자열 비교로 처리합니다.
                }
            }

            return Comparer.DefaultInvariant.Compare(
                Convert.ToString(left),
                Convert.ToString(right));
        }
    }
}
