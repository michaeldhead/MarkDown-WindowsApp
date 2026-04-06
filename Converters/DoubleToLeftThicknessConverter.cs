using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GHSMarkdownEditor.Converters;

/// <summary>
/// Converts a <see cref="double"/> to a <see cref="Thickness"/> with that value on the
/// left side only and zero on all other sides. Used by the outline panel to indent heading
/// items based on their level — e.g. an H2 indent of 12px becomes <c>Thickness(12,0,0,0)</c>.
/// </summary>
public class DoubleToLeftThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double d ? new Thickness(d, 0, 0, 0) : new Thickness(0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
