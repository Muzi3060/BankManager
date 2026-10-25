using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Media;

namespace BankManager;
internal static class Ui
{
    public static TextBlock Text(string text, string? style = null)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (style is not null) block.Classes.Add(style);
        return block;
    }
    public static StackPanel Stack(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 14 };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }
    public static StackPanel Row(params Control[] children)
    {
        var panel = Stack(children); panel.Orientation = Orientation.Horizontal; panel.Spacing = 10; return panel;
    }
    public static Border Card(Control content)
    {
        var border = new Border { Child = content }; border.Classes.Add("card"); return border;
    }
    public static Button Button(string text, Action action, string? style = null)
    {
        var button = new Button { Content = text };
        if (style is not null) button.Classes.Add(style);
        button.Click += (_, _) => action(); return button;
    }
    public static TextBox Input(string? text = null, string? placeholder = null, int max = 120) => new() { Text = text ?? "", PlaceholderText = placeholder, MaxLength = max };
    public static Control Field(string title, Control input, string? hint = null)
    {
        AutomationProperties.SetName(input, title);
        if (hint is not null) AutomationProperties.SetHelpText(input, hint);
        return hint is null ? Stack(Text(title), input) : Stack(Text(title), input, Text(hint, "muted"));
    }
    public static Grid Columns(params Control[] children)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", children.Select(_ => "*"))) };
        for (int i = 0; i < children.Length; i++)
        {
            children[i].Margin = new Thickness(i == 0 ? 0 : 8, 0, i == children.Length - 1 ? 0 : 8, 0);
            Grid.SetColumn(children[i], i); grid.Children.Add(children[i]);
        }
        return grid;
    }
    public static ComboBox Combo(IEnumerable<string> options, int index = 0) => new() { ItemsSource = options.ToArray(), SelectedIndex = index };
    public static Window Dialog(string title, Control content, double width = 620) => new()
    {
        Title = title, Width = width, Height = 720, MinWidth = 480, MinHeight = 400,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Content = new ScrollViewer { Content = new Border { Padding = new Thickness(28), Child = content } }
    };
    public static async Task<bool> Confirm(Window owner, string title, string message)
    {
        var panel = Stack(Text(title, "section"), Text(message, "muted"));
        var dialog = Dialog(title, panel); dialog.Height = 320;
        panel.Children.Add(Row(Button("Cancelar", () => dialog.Close(false)), Button("Confirmar", () => dialog.Close(true), "primary")));
        return await dialog.ShowDialog<bool>(owner);
    }
}
