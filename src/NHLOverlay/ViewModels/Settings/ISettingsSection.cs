using NHLOverlay.Models;

namespace NHLOverlay.ViewModels.Settings;

/// <summary>One tab of the settings window: loads its fields from settings and writes them back on save.</summary>
public interface ISettingsSection
{
    void Load(AppSettings settings);

    void Apply(AppSettings settings);
}
