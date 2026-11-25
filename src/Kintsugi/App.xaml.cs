using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kintsugi.Services;
using Kintsugi.Views;

namespace Kintsugi;

public partial class App : Application
{
    private const string AgreementText =
        "1. APPLICATION INTENT: This utility is designed to repair and patch bugged security scenarios that are otherwise inaccessible.\n\n" +
        "2. KERNEL MODIFICATION: Execution involves direct low-level overrides of Kernel-mode Security Scenarios within the system configuration.\n\n" +
        "3. UI BYPASS: User acknowledges that this tool bypasses standard Windows UI restrictions to resolve 'greyed out' or restricted states.\n\n" +
        "4. HARDWARE VALIDATION: While the tool performs a hardware compatibility check, manual bypass of warnings is at the user's discretion.\n\n" +
        "5. SYSTEM SCENARIOS: Targeted modifications focus on Kernel-mode Hardware-enforced Stack Protection and Memory Integrity structures.\n\n" +
        "6. INSTABILITY RISK: Modification of kernel-level parameters carries a risk of system instability if hardware does not fully support the patch.\n\n" +
        "7. BOOT INTEGRITY: The developer is not liable for failures to initialize the OS boot-loader, potential system boot loops or any other damages.\n\n" +
        "8. THIRD-PARTY STATUS: User acknowledges this is an unofficial tool and not a product endorsed by Microsoft Corporation.\n\n" +
        "9. ELEVATED ACCESS: Direct system-level access requires administrative privileges to modify restricted security scenario data.\n\n" +
        "10. DEPLOYMENT FINALIZATION: A full system restart is required to synchronize with the newly applied configuration.\n\n" +
        "Do you understand and authorize the execution of these security patches?";

    private Mutex? singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

#if DEBUG
        // Development aid: "Kintsugi.exe --render out.png" renders the main window to an image and exits,
        // without elevation, startup checks or the agreement. Not compiled into Release builds.
        if (e.Args is ["--render", var output])
        {
            _ = RenderMainWindowAsync(output);
            return;
        }

        if (e.Args is ["--render-dialog", var dialogOutput])
        {
            _ = RenderDialogAsync(dialogOutput);
            return;
        }
#endif

        // Only one copy may change security settings at a time.
        singleInstance = new Mutex(true, @"Global\Kintsugi.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            Dialog.Show(null, "Kintsugi is already running", "Switch to the open Kintsugi window.");
            Shutdown();
            return;
        }

        if (!PassesStartupChecks() || !HasAcceptedAgreement())
        {
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static bool PassesStartupChecks()
    {
        // The manifest requests elevation; this catches cases where it was bypassed.
        if (!WindowsEnvironment.IsElevated())
        {
            Dialog.Show(null, "Administrator rights required",
                "Kintsugi must be run as administrator.\n\nRight-click Kintsugi.exe and choose \"Run as administrator\".", DialogKind.Error);
            return false;
        }

        int build = WindowsEnvironment.GetBuildNumber();
        if (build < WindowsEnvironment.MinimumBuild)
        {
            Dialog.Show(null, "Windows 11 22H2 or newer required",
                $"Kintsugi requires Windows 11 22H2 (build {WindowsEnvironment.MinimumBuild}) or newer.\n\nDetected build: {build}", DialogKind.Error);
            return false;
        }

        Version? securityApp = WindowsEnvironment.GetSecurityAppVersion();
        if (securityApp is not null && securityApp < WindowsEnvironment.MinimumSecurityAppVersion)
        {
            Dialog.Show(null, "Windows Security app update required",
                $"Windows Security app version {WindowsEnvironment.MinimumSecurityAppVersion} or newer is required.\n\n" +
                $"Installed: {securityApp}\n\nUpdate it from the Microsoft Store.", DialogKind.Error);
            return false;
        }

        return true;
    }

    private static bool HasAcceptedAgreement()
    {
        if (AppSettings.HasAcceptedAgreement)
        {
            return true;
        }

        if (!Dialog.Ask(null, "Kintsugi: deployment protocols", AgreementText, "I authorize", "Exit", DialogKind.Warning))
        {
            return false;
        }

        AppSettings.HasAcceptedAgreement = true;
        return true;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Dialog.Show(MainWindow, "Unexpected error", e.Exception.Message, DialogKind.Error);
        e.Handled = true;
    }

#if DEBUG
    private async Task RenderMainWindowAsync(string output)
    {
        var window = new MainWindow { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
        window.Show();
        await Task.Delay(300);
        await window.FirstRefresh;
        await Task.Delay(300);

        SaveWindow(window, output);
        Shutdown();
    }

    private async Task RenderDialogAsync(string output)
    {
        Window dialog = Dialog.CreateForPreview("Kintsugi: deployment protocols", AgreementText, "I authorize", "Exit", DialogKind.Warning);
        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        dialog.Left = -10000;
        dialog.Top = -10000;
        dialog.Show();
        await Task.Delay(500);
        SaveWindow(dialog, output);
        Shutdown();
    }

    private static void SaveWindow(Window window, string output)
    {
        var content = (FrameworkElement)window.Content;
        double scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
        var bitmap = new RenderTargetBitmap((int)(content.ActualWidth * scale), (int)(content.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (DrawingContext dc = background.RenderOpen())
        {
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        }
        bitmap.Render(background);
        bitmap.Render(content);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream file = File.Create(output))
        {
            encoder.Save(file);
        }
    }
#endif
}
