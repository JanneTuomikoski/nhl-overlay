using NHLOverlay.Helpers;
using NHLOverlay.Models;
using NHLOverlay.Services;

namespace NHLOverlay.ViewModels.Settings;

/// <summary>Morning recap and spoiler shield.</summary>
public sealed class RecapSectionViewModel : ObservableObject, ISettingsSection
{
    private bool _recapEnabled;
    private double _showUntilHour;
    private bool _showOnYesterday;
    private bool _shieldEnabled;

    public RecapSectionViewModel(TeamDirectoryService teams, LogoService logos)
    {
        ShieldTeamGroups = TeamsSectionViewModel.BuildTeamGroups(teams, logos);
        ShieldTeams = ShieldTeamGroups.SelectMany(g => g.Teams).ToList();
    }

    public bool RecapEnabled { get => _recapEnabled; set => SetProperty(ref _recapEnabled, value); }

    public double ShowUntilHour { get => _showUntilHour; set => SetProperty(ref _showUntilHour, Math.Round(value)); }

    public bool ShowOnYesterday { get => _showOnYesterday; set => SetProperty(ref _showOnYesterday, value); }

    public bool ShieldEnabled { get => _shieldEnabled; set => SetProperty(ref _shieldEnabled, value); }

    /// <summary>Teams whose games are always hidden until revealed.</summary>
    public IReadOnlyList<TeamGroup> ShieldTeamGroups { get; }

    private IReadOnlyList<TeamChoiceViewModel> ShieldTeams { get; }

    public void Load(AppSettings settings)
    {
        RecapEnabled = settings.Recap.Enabled;
        ShowUntilHour = settings.Recap.ShowUntilHour;
        ShowOnYesterday = settings.Recap.ShowOnYesterday;
        ShieldEnabled = settings.Spoilers.Enabled;
        TeamsSectionViewModel.SelectTeams(ShieldTeams, settings.Spoilers.AlwaysShieldTeams);
    }

    public void Apply(AppSettings settings)
    {
        settings.Recap.Enabled = RecapEnabled;
        settings.Recap.ShowUntilHour = (int)ShowUntilHour;
        settings.Recap.ShowOnYesterday = ShowOnYesterday;
        settings.Spoilers.Enabled = ShieldEnabled;
        settings.Spoilers.AlwaysShieldTeams = TeamsSectionViewModel.SelectedTeams(ShieldTeams, settings.Spoilers.AlwaysShieldTeams);
    }
}
