using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using NHLOverlay.Helpers;
using NHLOverlay.Models;

namespace NHLOverlay.ViewModels;

/// <summary>A notification shown inside the overlay.</summary>
public sealed class ToastViewModel
{
    public ToastViewModel(GameEventKind? kind, string title, string body, DateTimeOffset expires, Action<ToastViewModel> dismiss)
    {
        Kind = kind;
        Title = title;
        Body = body;
        Expires = expires;
        DismissCommand = new RelayCommand(() => dismiss(this));
    }

    /// <summary>Null for summaries (e.g. after Do Not Disturb).</summary>
    public GameEventKind? Kind { get; }

    public string Title { get; }

    public string Body { get; }

    public bool HasBody => Body.Length > 0;

    public DateTimeOffset Expires { get; }

    public ICommand DismissCommand { get; }
}

/// <summary>Holds the visible overlay toasts and expires them. UI thread only.</summary>
public sealed class ToastService
{
    public const int MaxVisible = 3;

    private readonly DispatcherTimer _timer;

    public ToastService(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Expire(), dispatcher);
        _timer.Stop();
    }

    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    public void Show(GameEventKind? kind, string title, string body, TimeSpan duration)
    {
        Toasts.Insert(0, new ToastViewModel(kind, title, body, DateTimeOffset.UtcNow + duration, Remove));
        while (Toasts.Count > MaxVisible) Toasts.RemoveAt(Toasts.Count - 1);
        _timer.Start();
    }

    public void Remove(ToastViewModel toast)
    {
        Toasts.Remove(toast);
        if (Toasts.Count == 0) _timer.Stop();
    }

    private void Expire()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var toast in Toasts.Where(t => t.Expires <= now).ToList()) Toasts.Remove(toast);
        if (Toasts.Count == 0) _timer.Stop();
    }
}
