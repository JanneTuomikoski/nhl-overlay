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

    public event EventHandler? ToggleOverlayRequested;
    public event EventHandler? ToggleInteractionRequested;
    public event EventHandler? ToggleDisplayModeRequested;
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
