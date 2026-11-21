using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SmartOepnv.AppShared.Helpers;

/// <summary>
/// Suchtext vom Feld zur Quelle, ohne den Feldinhalt zurückzuschreiben.
/// Ein normales TwoWay-Binding setzt den Text mitten im Tastendruck neu und verschluckt Zeichen.
/// </summary>
public static class LiveText
{
    static LiveText()
    {
        EventManager.RegisterClassHandler(
            typeof(TextBox),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnBoxLoaded));
    }

    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source",
        typeof(string),
        typeof(LiveText),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSourceChanged,
            CoerceSource));

    private static readonly DependencyProperty HookedProperty =
        DependencyProperty.RegisterAttached("Hooked", typeof(bool), typeof(LiveText));

    private static readonly DependencyProperty WritingProperty =
        DependencyProperty.RegisterAttached("Writing", typeof(bool), typeof(LiveText));

    public static void SetSource(DependencyObject element, string? value) =>
        element.SetCurrentValue(SourceProperty, value ?? string.Empty);

    public static string GetSource(DependencyObject element) =>
        (string)(element.GetValue(SourceProperty) ?? string.Empty);

    private static object CoerceSource(DependencyObject d, object baseValue)
    {
        if (d is TextBox box)
        {
            EnsureHook(box);
        }

        return baseValue ?? string.Empty;
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box)
        {
            return;
        }

        EnsureHook(box);
        if (box.GetValue(WritingProperty) is true)
        {
            return;
        }

        var text = e.NewValue as string ?? string.Empty;
        if (string.Equals(box.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        box.Text = text;
        box.CaretIndex = box.Text.Length;
    }

    private static void OnBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || BindingOperations.GetBinding(box, SourceProperty) is null)
        {
            return;
        }

        EnsureHook(box);
        box.DataContextChanged -= OnBoxDataContextChanged;
        box.DataContextChanged += OnBoxDataContextChanged;
        PushIfDifferent(box);
    }

    private static void OnBoxDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox box || BindingOperations.GetBinding(box, SourceProperty) is null)
        {
            return;
        }

        EnsureHook(box);
        PushIfDifferent(box);
    }

    private static void EnsureHook(TextBox box)
    {
        if (box.GetValue(HookedProperty) is true)
        {
            return;
        }

        box.SetValue(HookedProperty, true);
        box.TextChanged += OnTextChanged;
    }

    private static void PushIfDifferent(TextBox box)
    {
        var text = box.Text ?? string.Empty;
        if (string.Equals(ReadBoundSource(box) ?? GetSource(box), text, StringComparison.Ordinal))
        {
            return;
        }

        box.SetValue(WritingProperty, true);
        try
        {
            if (!TryWriteBoundSource(box, text))
            {
                SetSource(box, text);
                BindingOperations.GetBindingExpression(box, SourceProperty)?.UpdateSource();
            }
        }
        finally
        {
            box.SetValue(WritingProperty, false);
        }
    }

    private static void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box)
        {
            PushIfDifferent(box);
        }
    }

    private static string? ReadBoundSource(TextBox box)
    {
        if (!TryGetBoundProperty(box, out var source, out var property))
        {
            return null;
        }

        return property.GetValue(source) as string ?? string.Empty;
    }

    private static bool TryWriteBoundSource(TextBox box, string text)
    {
        if (!TryGetBoundProperty(box, out var source, out var property))
        {
            return false;
        }

        var current = property.GetValue(source) as string ?? string.Empty;
        if (!string.Equals(current, text, StringComparison.Ordinal))
        {
            property.SetValue(source, text);
        }

        return true;
    }

    private static bool TryGetBoundProperty(TextBox box, out object source, out PropertyInfo property)
    {
        source = null!;
        property = null!;
        var expression = BindingOperations.GetBindingExpression(box, SourceProperty);
        var resolved = expression?.ResolvedSource;
        var name = expression?.ResolvedSourcePropertyName;
        if (resolved is not null && !string.IsNullOrEmpty(name))
        {
            var found = FindStringProperty(resolved, name);
            if (found is not null)
            {
                source = resolved;
                property = found;
                return true;
            }
        }

        var path = expression?.ParentBinding.Path.Path;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        object? root = expression?.DataItem ?? box.DataContext;
        if (root is null)
        {
            return false;
        }

        var parts = path.Split('.');
        var current = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var step = current.GetType().GetProperty(parts[i], BindingFlags.Instance | BindingFlags.Public);
            var next = step?.GetValue(current);
            if (next is null)
            {
                return false;
            }

            current = next;
        }

        var last = FindStringProperty(current, parts[^1]);
        if (last is null)
        {
            return false;
        }

        source = current;
        property = last;
        return true;
    }

    private static PropertyInfo? FindStringProperty(object target, string name)
    {
        var found = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (found is null || !found.CanWrite || found.PropertyType != typeof(string))
        {
            return null;
        }

        return found;
    }
}
