using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

        // Nested choices (team chips, countries, ...) change without their tab noticing, so any edit in the
        // window schedules a live preview; a preview that changes nothing is skipped.
        RoutedEventHandler edited = (_, _) => viewModel.SchedulePreview();
        AddHandler(ToggleButton.CheckedEvent, edited, handledEventsToo: true);
        AddHandler(ToggleButton.UncheckedEvent, edited, handledEventsToo: true);
        AddHandler(ButtonBase.ClickEvent, edited, handledEventsToo: true);
        AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => viewModel.SchedulePreview()), handledEventsToo: true);
        AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => viewModel.SchedulePreview()), handledEventsToo: true);
        Closed += (_, _) => viewModel.Dispose();
    }
}
