using System.Windows;
using Forms = System.Windows.Forms;

namespace NHLOverlay.Helpers;

/// <summary>System tray icon and menu (Windows Forms NotifyIcon, hosted in the WPF dispatcher loop).</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _toggleOverlayItem;
    private readonly Forms.ToolStripMenuItem _interactionItem;
    private readonly Forms.ToolStripMenuItem _expandedItem;
    private readonly Forms.ToolStripMenuItem _filterItem;
    private readonly Forms.ToolStripMenuItem _doNotDisturbItem;

    public event EventHandler? ToggleOverlayRequested;
    public event EventHandler? ToggleInteractionRequested;
    public event EventHandler? ToggleDisplayModeRequested;
    public event EventHandler? ToggleDoNotDisturbRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public TrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        var title = new Forms.ToolStripLabel("NHL Live Overlay");
        title.Font = new System.Drawing.Font(title.Font, System.Drawing.FontStyle.Bold);
        menu.Items.Add(title);
        menu.Items.Add(new Forms.ToolStripSeparator());
        _toggleOverlayItem = new Forms.ToolStripMenuItem("Hide overlay", null, (_, _) => ToggleOverlayRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(_toggleOverlayItem);
        _interactionItem = new Forms.ToolStripMenuItem("Interaction mode", null, (_, _) => ToggleInteractionRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(_interactionItem);
        _expandedItem = new Forms.ToolStripMenuItem("Expanded view", null, (_, _) => ToggleDisplayModeRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(_expandedItem);
        _filterItem = new Forms.ToolStripMenuItem("Show");
        menu.Items.Add(_filterItem);
        _doNotDisturbItem = new Forms.ToolStripMenuItem("Do not disturb", null, (_, _) => ToggleDoNotDisturbRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(_doNotDisturbItem);
        menu.Items.Add(new Forms.ToolStripMenuItem("Refresh now", null, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Forms.ToolStripMenuItem("Settings...", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "NHL Live Overlay",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                ToggleOverlayRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    public void SetOverlayVisible(bool visible) =>
        _toggleOverlayItem.Text = visible ? "Hide overlay" : "Show overlay";

    /// <summary>Checked = the overlay accepts mouse input; unchecked = click-through.</summary>
    public void SetInteractionMode(bool interactive) => _interactionItem.Checked = interactive;

    public void SetExpanded(bool expanded) => _expandedItem.Checked = expanded;

    public void SetDoNotDisturb(bool enabled) => _doNotDisturbItem.Checked = enabled;

    /// <summary>Windows notification (shown as a toast on Windows 10/11).</summary>
    public void ShowNotification(string title, string body)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(6000, title, string.IsNullOrWhiteSpace(body) ? " " : body, Forms.ToolTipIcon.None);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Log.Warn("Could not show a Windows notification.", ex);
        }
    }

    /// <summary>Rebuilds the "Show" submenu (game filter choices).</summary>
    public void SetFilterChoices(IEnumerable<(string Label, bool IsChecked, Action Select)> choices)
    {
        _filterItem.DropDownItems.Clear();
        foreach (var (label, isChecked, select) in choices)
            _filterItem.DropDownItems.Add(new Forms.ToolStripMenuItem(label, null, (_, _) => select()) { Checked = isChecked });
    }

    /// <summary>Tooltip text is limited to 127 characters by Windows.</summary>
    public void SetToolTip(string text) =>
        _notifyIcon.Text = text.Length > 127 ? text[..127] : text;

    private static System.Drawing.Icon LoadIcon()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        using var stream = resource!.Stream;
        return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Dispose();
    }
}
