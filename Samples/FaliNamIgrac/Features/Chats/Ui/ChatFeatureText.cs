namespace FaliNamIgrac;

// Agent 11 supplies localized values when constructing ChatNotifier.
public sealed record ChatFeatureText(
    string GroupPhoto = "Choose group photo",
    string PhotoSelected = "Group photo selected",
    string DeleteEveryone = "Delete conversation for everyone",
    string ConfirmDeleteEveryone = "Confirm: everyone will lose access to this conversation",
    string CachedHistory = "Showing cached messages. Refresh to load the complete history.",
    string Read = "Read",
    string Sent = "Sent"
);
