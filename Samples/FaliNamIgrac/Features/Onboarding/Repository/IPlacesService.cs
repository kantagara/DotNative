namespace FaliNamIgrac;

public interface IPlacesService
{
    Task<IReadOnlyList<PlaceSuggestion>> SearchAsync(string text, CancellationToken token);
    Task<SelectedPlace> GetDetailsAsync(PlaceSuggestion place, CancellationToken token);
}
