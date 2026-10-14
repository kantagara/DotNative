using Microsoft.Extensions.DependencyInjection;

namespace MauiComparison;

public sealed class CartPage : ContentPage
{
    private IServiceScope? _scope;

    public CartPage(SampleStore store, CartVisit visit)
    {
        Title = "Your cart";
        SetDynamicResource(BackgroundColorProperty, "Background");
        var note = Ui.Input(visit.Note, "Type something…");
        note.TextChanged += (_, e) => visit.Note = e.NewTextValue;
        var content = Ui.Stack(
            Ui.Text("The same cart, wherever you are."),
            Ui.Card(
                Ui.Stack(
                    Ui.Bound(store, nameof(store.CartSummary), 36),
                    Ui.Button("Add item", store.Add, true),
                    Ui.Button("Remove item", store.Remove)
                ),
                20
            ),
            Ui.Text("A note for this visit", 22),
            Ui.Card(note, 14),
            Ui.Text(
                "Your cart stays when you go back. This note starts fresh each time you open the cart.",
                15
            ),
            Ui.Themes()
        );
        content.Padding = 24;
        Content = new ScrollView { Content = content };
    }

    internal void OwnScope(IServiceScope scope) => _scope = scope;

    internal void ReleaseScope()
    {
        _scope?.Dispose();
        _scope = null;
    }
}
