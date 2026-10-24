// Adapted from Pinyon Shift's launcher (launcher/PinyonShift.Launcher.Desktop, BSD-3-Clause; see THIRD_PARTY_NOTICES.md).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Fm4StaticRecomp.Launcher;

/// <summary>A small modal window with a title, the text and one button per answer. Returns the
/// answer chosen, or null when the window was closed. The first answer has the focus, so Enter
/// takes it; Escape closes.</summary>
public static class Dialog
{
    public static Task<string?> AskAsync(Window owner, string title, string message, params string[] answers) =>
        AskAsync(owner, title, message, [], answers);

    // Link buttons open their URL and leave the dialog open.
    public static async Task<string?> AskAsync(Window owner, string title, string message,
        IReadOnlyList<(string Label, string Url)> links, params string[] answers)
    {
        var (answer, _) = await ShowAsync(owner, title, message, links, [], answers);
        return answer;
    }

    // Text boxes under the message; the values are returned as typed, with the answer.
    public static Task<(string? Answer, IReadOnlyList<string> Values)> PromptAsync(Window owner, string title, string message,
        IReadOnlyList<(string Label, string Value)> fields, params string[] answers) =>
        ShowAsync(owner, title, message, [], fields, answers);

    private static async Task<(string? Answer, IReadOnlyList<string> Values)> ShowAsync(Window owner, string title,
        string message, IReadOnlyList<(string Label, string Url)> links, IReadOnlyList<(string Label, string Value)> fields,
        string[] answers)
    {
        string? chosen = null;
        var window = new Window
        {
            Title = title,
            Width = 540,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = owner.Icon,
            Background = (IBrush?)owner.FindResource("SurfaceBrush"),
        };
        var body = new StackPanel { Margin = new Thickness(26, 22), Spacing = 14 };
        body.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), Classes = { "dialogTitle" }, TextWrapping = TextWrapping.Wrap });
        if (message.Length > 0)
            body.Children.Add(new SelectableTextBlock
            {
                Text = message, FontSize = 15, TextWrapping = TextWrapping.Wrap, LineHeight = 21,
                Foreground = (IBrush?)owner.FindResource("ValueBrush"),
            });
        if (links.Count > 0)
        {
            var row = new WrapPanel();
            foreach (var (label, url) in links)
            {
                var link = new Button { Content = label.ToUpperInvariant(), Margin = new Thickness(0, 0, 10, 0) };
                link.Classes.Add("link");
                link.Click += (_, _) => LauncherHost.Open(url);
                row.Children.Add(link);
            }
            body.Children.Add(row);
        }
        var boxes = new List<TextBox>();
        foreach (var (label, value) in fields)
        {
            body.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), Classes = { "header" } });
            var box = new TextBox { Text = value, MaxLength = 32 };
            boxes.Add(box);
            body.Children.Add(box);
        }
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right,
        };
        Button? first = null;
        foreach (var answer in answers)
        {
            var button = new Button { Content = answer, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
            button.Classes.Add("quiet");
            button.Click += (_, _) => { chosen = answer; window.Close(); };
            buttons.Children.Add(button);
            first ??= button;
        }
        body.Children.Add(buttons);
        window.Content = body;
        window.KeyDown += (_, e) => { if (e.Key == Key.Escape) window.Close(); };
        window.Opened += (_, _) => { if (boxes.Count > 0) boxes[0].Focus(); else first?.Focus(); };
        await window.ShowDialog(owner);
        return (chosen, boxes.Select(b => b.Text ?? "").ToList());
    }
}
