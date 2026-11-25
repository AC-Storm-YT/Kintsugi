using System.Windows;
using System.Windows.Media;

namespace Kintsugi.Views;

public enum DialogKind
{
    Info,
    Question,
    Warning,
    Error
}

/// <summary>Themed replacement for MessageBox.</summary>
public partial class Dialog : Window
{
    private Dialog(Window? owner, DialogKind kind, string heading, string message, string primary, string? secondary)
    {
        InitializeComponent();

        if (owner is { IsLoaded: true })
        {
            Owner = owner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        Heading.Text = heading;
        Message.Text = message;
        PrimaryButton.Content = primary;

        if (secondary is null)
        {
            SecondaryButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            SecondaryButton.Content = secondary;
        }

        (IconGlyph.Text, IconGlyph.Foreground) = kind switch
        {
            DialogKind.Warning => ("", (Brush)FindResource("WarnBrush")),
            DialogKind.Error => ("", (Brush)FindResource("BadBrush")),
            DialogKind.Question => ("", (Brush)FindResource("AccentBrush")),
            _ => ("", (Brush)FindResource("AccentBrush"))
        };
    }

    private void Primary_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Secondary_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Asks a yes/no question. Returns true when the primary button is chosen.</summary>
    public static bool Ask(Window? owner, string heading, string message,
        string primary = "Yes", string secondary = "No", DialogKind kind = DialogKind.Question) =>
        new Dialog(owner, kind, heading, message, primary, secondary).ShowDialog() == true;

#if DEBUG
    /// <summary>Creates a dialog without showing it modally (used by the screenshot mode).</summary>
    public static Window CreateForPreview(string heading, string message, string primary, string? secondary, DialogKind kind) =>
        new Dialog(null, kind, heading, message, primary, secondary);
#endif

    /// <summary>Shows a message with a single OK button.</summary>
    public static void Show(Window? owner, string heading, string message, DialogKind kind = DialogKind.Info) =>
        new Dialog(owner, kind, heading, message, "OK", null).ShowDialog();
}
