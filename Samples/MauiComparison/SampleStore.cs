using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MauiComparison;

public sealed class SampleStore : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public int Count { get; private set; }
    public int CompletedVisits { get; private set; }
    public string Summary => $"Items in cart: {Count}";
    public string CartSummary => $"{Count} items";
    public string VisitSummary => $"Completed visits: {CompletedVisits}";

    public void Add()
    {
        Count++;
        NotifyCount();
    }

    public void Remove()
    {
        Count = Math.Max(0, Count - 1);
        NotifyCount();
    }

    public void CompleteVisit()
    {
        CompletedVisits++;
        Notify(nameof(VisitSummary));
    }

    private void NotifyCount()
    {
        Notify(nameof(Summary));
        Notify(nameof(CartSummary));
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class CartVisit(SampleStore store) : IDisposable
{
    private bool _disposed;
    public string Note { get; set; } = "";

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        store.CompleteVisit();
    }
}
