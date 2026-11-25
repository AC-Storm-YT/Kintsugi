using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kintsugi.Services;

namespace Kintsugi.Views;

public partial class MainWindow : Window
{
    private SystemStatus? status;
    private bool restorePointCreated;
    private bool busy;

    public MainWindow()
    {
        InitializeComponent();
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
        VersionRun.Text = $"Kintsugi {version}";
        Loaded += async (_, _) => await OnLoadedAsync(version);
    }

    /// <summary>Completes when the first status refresh has finished (used by the screenshot mode).</summary>
    public Task FirstRefresh { get; private set; } = Task.CompletedTask;

    private async Task OnLoadedAsync(string version)
    {
        Log($"Kintsugi {version} started on Windows build {WindowsEnvironment.GetBuildNumber()}.");
        FirstRefresh = RunBusyAsync(RefreshStatusAsync);
        await FirstRefresh;

        if (status is { PolicyOverrides.Count: > 0 })
        {
            Log("Device Guard policy values found (these can grey out the Windows Security toggles):");
            foreach (string value in status.PolicyOverrides)
            {
                Log("    " + value);
            }
        }
    }

    // ------------------------------------------------------------------ status

    private async Task RefreshStatusAsync()
    {
        status = await Task.Run(SystemStatus.Read);

        if (status.DeviceGuardError is not null)
        {
            Log("Could not read live Device Guard status: " + status.DeviceGuardError);
        }

        ShowFeature(MiPill, MiPillText, MiToggle, status.MemoryIntegrity);
        ShowFeature(KspPill, KspPillText, KspToggle, status.KernelStackProtection);

        (VbsValue.Text, VbsValue.Foreground) = status.VbsStatus switch
        {
            2 => ("Running", Brush("GoodBrush")),
            1 => ("Enabled, not running", Brush("WarnBrush")),
            0 => ("Off", Brush("NeutralBrush")),
            _ => ("Unknown", Brush("NeutralBrush"))
        };
        (VirtValue.Text, VirtValue.Foreground) = YesNo(status.VirtualizationAvailable, "Available", "Not available");
        (ShadowValue.Text, ShadowValue.Foreground) = YesNo(status.ShadowStacksSupported, "Supported", "Not supported");
        CpuValue.Text = status.CpuName;
        CpuValue.ToolTip = status.CpuName;
        WindowsValue.Text = $"Build {WindowsEnvironment.GetBuildNumber()}";

        int policies = status.PolicyOverrides.Count;
        (PolicyValue.Text, PolicyValue.Foreground) = policies == 0
            ? ("None", Brush("GoodBrush"))
            : ($"{policies} value{(policies == 1 ? "" : "s")} set", Brush("WarnBrush"));
        PolicyValue.ToolTip = policies == 0 ? null : string.Join(Environment.NewLine, status.PolicyOverrides);

        RestartBanner.Visibility = status.AnyPendingRestart ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowFeature(Border pill, TextBlock pillText, CheckBox toggle, FeatureState state)
    {
        (string text, string tone) = state switch
        {
            { Running: null } => (state.IsConfiguredOn ? "Enabled" : "Off", state.IsConfiguredOn ? "Good" : "Neutral"),
            { Running: true, IsConfiguredOn: true } => ("On", "Good"),
            { Running: true } => ("Off after restart", "Warn"),
            { IsConfiguredOn: true } => ("Restart required", "Warn"),
            _ => ("Off", "Neutral")
        };

        pillText.Text = text;
        pill.Tag = tone;
        pill.ToolTip = state.IsConfiguredOn && state.Running == false
            ? "Configured on but not running yet. If it is still not running after a restart, a driver or a firmware setting may be blocking it."
            : null;
        toggle.IsChecked = state.IsConfiguredOn;
    }

    private (string, Brush) YesNo(bool? value, string yes, string no) => value switch
    {
        true => (yes, Brush("GoodBrush")),
        false => (no, Brush("BadBrush")),
        null => ("Unknown", Brush("NeutralBrush"))
    };

    private Brush Brush(string key) => (Brush)FindResource(key);

    // ------------------------------------------------------------------ actions

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        var toggle = (CheckBox)sender;
        bool enable = toggle.IsChecked == true;
        toggle.IsChecked = !enable;   // show the real state until the change is confirmed and applied

        var feature = Enum.Parse<SecurityFeature>((string)toggle.Tag);
        await RunBusyAsync(() => ApplyAsync(feature, enable));
    }

    private async Task ApplyAsync(SecurityFeature feature, bool enable)
    {
        string name = SecurityFeatureInfo.DisplayName(feature);
        string verb = enable ? "Enable" : "Disable";
        Log($"--- {verb} {name} ---");

        await RefreshStatusAsync();
        FeatureState state = status!.Get(feature);

        if (state.IsConfiguredOn == enable)
        {
            Log(state.PendingRestart
                ? $"{name} is already set to {(enable ? "on" : "off")}. Restart Windows to apply it."
                : $"{name} is already {(enable ? "on" : "off")}. Nothing to do.");
            return;
        }

        if (enable && !ConfirmEnablePrerequisites(feature))
        {
            return;
        }

        List<SecurityFeature> features = [feature];
        if (!enable && feature == SecurityFeature.MemoryIntegrity && status.KernelStackProtection.IsConfiguredOn)
        {
            if (!Dialog.Ask(this, "Kernel Stack Protection will also be disabled",
                    "Kernel Stack Protection depends on Memory Integrity, so it will be disabled as well.",
                    "Continue", "Cancel", DialogKind.Warning))
            {
                Log("Cancelled.");
                return;
            }

            features.Add(SecurityFeature.KernelStackProtection);
        }

        string summary = string.Join(" and ", features.Select(SecurityFeatureInfo.DisplayName));
        if (!Dialog.Ask(this, $"{verb} {summary}?", "Windows must restart for the change to take effect.", verb, "Cancel"))
        {
            Log("Cancelled.");
            return;
        }

        if (!restorePointCreated &&
            Dialog.Ask(this, "Create a restore point first?",
                "Recommended. A restore point lets you roll back if Windows fails to start after the change.",
                "Create restore point", "Skip"))
        {
            if (!await CreateRestorePointAsync() &&
                !Dialog.Ask(this, "Restore point failed", "The restore point could not be created. Apply the change anyway?",
                    "Apply anyway", "Cancel", DialogKind.Warning))
            {
                Log("Cancelled.");
                return;
            }
        }

        try
        {
            bool removedLegacyPolicy = await Task.Run(() =>
                features.Aggregate(false, (removed, f) => FeatureController.SetEnabled(f, enable) | removed));

            if (removedLegacyPolicy)
            {
                Log($"Removed the legacy policy value {FeatureController.LegacyPolicyValue} written by older versions.");
            }

            Log($"SUCCESS: {summary} {(enable ? "enabled" : "disabled")}. Restart required.");
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
            Dialog.Show(this, "The change could not be applied", ex.Message, DialogKind.Error);
            return;
        }

        await RefreshStatusAsync();
        if (Dialog.Ask(this, "Restart now?", "Windows must restart to apply the change. Save your work before restarting.",
                "Restart now", "Later"))
        {
            Restart();
        }
    }

    private bool ConfirmEnablePrerequisites(SecurityFeature feature)
    {
        SystemStatus s = status!;

        if (feature == SecurityFeature.MemoryIntegrity)
        {
            if (s.VirtualizationAvailable != false)
            {
                return true;
            }

            Log("WARNING: Windows reports that hardware virtualization is not available.");
            return ConfirmForce("Hardware virtualization is not available",
                "Enable Intel VT-x or AMD SVM in your firmware (BIOS/UEFI) settings first. Without it, Memory Integrity will not start.");
        }

        // Kernel Stack Protection needs Memory Integrity actually running, not just configured.
        FeatureState hvci = s.MemoryIntegrity;
        if (!(hvci.Running ?? hvci.IsConfiguredOn))
        {
            string why = hvci.IsConfiguredOn
                ? "Memory Integrity is enabled but not running yet. Restart Windows, then try again."
                : "Turn on Memory Integrity and restart Windows before enabling Kernel Stack Protection.";
            Log("BLOCKED: " + why);
            Dialog.Show(this, "Memory Integrity is required", why, DialogKind.Warning);
            return false;
        }

        if (s.ShadowStacksSupported == true)
        {
            Log($"Hardware shadow stacks supported ({s.CpuName}).");
            return true;
        }

        string reason = s.ShadowStacksSupported == false
            ? "Windows reports that this processor does not support hardware shadow stacks (Intel CET, or AMD Zen 3 and newer)."
            : "Hardware shadow stack support could not be verified on this system.";
        Log("WARNING: " + reason);
        return ConfirmForce("Unsupported hardware", $"{s.CpuName}\n\n{reason}");
    }

    private bool ConfirmForce(string heading, string reason)
    {
        bool force = Dialog.Ask(this, heading, reason + "\n\nForcing this may cause a boot loop or a blue screen.",
            "Proceed anyway", "Cancel", DialogKind.Warning);
        Log(force ? "User chose to proceed despite the warning." : "Cancelled after the warning.");
        return force;
    }

    // ------------------------------------------------------------------ tools

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            await RefreshStatusAsync();
            Log("Status refreshed.");
        });
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            Log("--- Driver conflict scan ---");
            List<DriverFinding> findings = await Task.Run(() => DriverScanner.Scan(Log));

            const string disclaimer = "This scan is a heuristic: it may report false positives and cannot find every incompatible driver.";
            if (findings.Count == 0)
            {
                Log("No known problem drivers found.");
                Dialog.Show(this, "No known conflicts found", "No drivers known to conflict with Memory Integrity were found.\n\n" + disclaimer);
                return;
            }

            Log($"{findings.Count} possible conflict(s):");
            findings.ForEach(f => Log("[!] " + f));
            Dialog.Show(this, $"{findings.Count} possible driver conflict{(findings.Count == 1 ? "" : "s")}",
                string.Join("\n", findings.Select(f => "•  " + f)) +
                "\n\nUpdate or uninstall the related software before enabling Memory Integrity.\n\n" + disclaimer,
                DialogKind.Warning);
        });
    }

    private async void RestorePoint_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(async () => await CreateRestorePointAsync());

    private async Task<bool> CreateRestorePointAsync()
    {
        Log("Creating System Restore point (this can take a minute)…");
        string? error = await Task.Run(() => RestorePoint.Create("Kintsugi: before security setting change"));
        if (error is null)
        {
            restorePointCreated = true;
            Log("Restore point created. (Windows skips it if another one was made in the last 24 hours.)");
            return true;
        }

        Log("Restore point failed: " + error);
        return false;
    }

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (Dialog.Ask(this, "Restart now?", "Save your work before restarting.", "Restart now", "Cancel"))
        {
            Restart();
        }
    }

    private void Restart()
    {
        Log("Restarting in 5 seconds…");
        WindowsEnvironment.RestartSoon();
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (LogBox.Text.Length > 0)
        {
            Clipboard.SetText(LogBox.Text);
        }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogBox.Clear();

    // ------------------------------------------------------------------ helpers

    /// <summary>Runs an operation with the controls disabled, so actions cannot overlap.</summary>
    private async Task RunBusyAsync(Func<Task> operation)
    {
        if (busy)
        {
            return;
        }

        busy = true;
        SetControlsEnabled(false);
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
        }
        finally
        {
            SetControlsEnabled(true);
            busy = false;
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        BusyBar.Visibility = enabled ? Visibility.Hidden : Visibility.Visible;
        foreach (UIElement element in new UIElement[] { MiToggle, KspToggle, ScanButton, RestorePointButton, RefreshButton })
        {
            element.IsEnabled = enabled;
        }
    }

    private void Log(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Log(text));
            return;
        }

        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }
}
