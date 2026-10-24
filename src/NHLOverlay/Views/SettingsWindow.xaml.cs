using System.Windows;
using NHLOverlay.Helpers;
using NHLOverlay.ViewModels;

namespace NHLOverlay.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
        SourceInitialized += (_, _) => NativeMethods.UseDarkTitleBar(this);
        Closed += (_, _) => viewModel.Dispose();
    }
}
