using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RemoteAnnotate.Client.Views;

/// <summary>
/// Resolves an icon name published by a view model, such as <c>Pause</c>, to the matching
/// <c>Icon.*</c> geometry in <c>Resources/Icons.xaml</c>. View models stay free of WPF types
/// and the icon set stays in that one dictionary.
/// </summary>
public sealed class IconKeyConverter : IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        _ = targetType;
        _ = parameter;
        _ = culture;
        return value is string name && Application.Current?.TryFindResource($"Icon.{name}") is { } icon
            ? icon
            : null;
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        _ = value;
        _ = targetType;
        _ = parameter;
        _ = culture;
        throw new NotSupportedException();
    }
}
