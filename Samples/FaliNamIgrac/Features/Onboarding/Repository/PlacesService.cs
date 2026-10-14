namespace FaliNamIgrac;

public sealed class PlacesService : IPlacesService, IDisposable
{
    private readonly HttpClient http = new()
    {
        BaseAddress = new Uri("https://maps.googleapis.com/maps/api/place/"),
        Timeout = TimeSpan.FromSeconds(100),
    };

    public void Dispose() => http.Dispose();

    public async Task<IReadOnlyList<PlaceSuggestion>> SearchAsync(
        string text,
        CancellationToken token
    )
    {
        using var response = await http.GetAsync(
            "autocomplete/json?"
                + ApiJson.QueryString(
                    new PlacesAutocompleteQuery(BackendConfiguration.PlacesKey, text)
                ),
            token
        );
        response.EnsureSuccessStatusCode();
        var result = await ApiJson.ReadAsync<PlacesAutocompleteResponse>(response.Content, token);
        return result
            .Predictions.Select(place => new PlaceSuggestion(place.Description, place.Id))
            .ToArray();
    }

    public async Task<SelectedPlace> GetDetailsAsync(PlaceSuggestion place, CancellationToken token)
    {
        using var response = await http.GetAsync(
            "details/json?"
                + ApiJson.QueryString(
                    new PlacesDetailsQuery(BackendConfiguration.PlacesKey, place.Id)
                ),
            token
        );
        response.EnsureSuccessStatusCode();
        var result = await ApiJson.ReadAsync<PlacesDetailsResponse>(response.Content, token);
        var location = result.Result.Geometry.Location;
        return new(place.Description, location.Lat, location.Lng);
    }
}
