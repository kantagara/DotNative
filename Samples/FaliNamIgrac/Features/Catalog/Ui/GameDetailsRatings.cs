using DotNative;

namespace FaliNamIgrac;

public sealed partial class GameDetailsScreen
{
    private readonly State<string?> ratingsGame = new(null);
    private readonly State<IReadOnlyDictionary<int, int>> ratingValues = new(
        new Dictionary<int, int>()
    );

    public void OpenRatings(string id)
    {
        ratingValues.Value = new Dictionary<int, int>();
        ratingsGame.Value = id;
    }

    private Element Ratings(string id)
    {
        var model = appointmentActions?.Model;
        var rows = new List<Element> { Label(Loc.AppointmentRate, 24, "Bold"), Gap(24) };
        if (model is null || model.GameId != id || model.State.Details is not { } game)
            rows.Add(Label(Loc.Loading));
        else if (model.State.RatingsSubmitted)
            rows.Add(Label(Loc.AppointmentRated));
        else if (model.Allows(AppointmentAction.Rate))
        {
            foreach (var player in game.RatingTargets(model.UserId))
            {
                rows.Add(Label(player.Name, 18, "Bold"));
                rows.Add(Gap(12));
                rows.Add(
                    new HStack(
                        Enumerable
                            .Range(1, 5)
                            .Select(value =>
                                (Element)
                                    new Button(
                                        ratingValues.Value.GetValueOrDefault(player.Id) >= value
                                            ? "\ue838"
                                            : "\ue83a",
                                        () =>
                                            ratingValues.Value = new Dictionary<int, int>(
                                                ratingValues.Value
                                            )
                                            {
                                                [player.Id] = value,
                                            }
                                    )
                                        .TextStyle(TextStyleFor("Icons"))
                                        .FontSize(40 * R)
                                        .ForegroundColor(new Color(255, 193, 7))
                                        .Width(48 * R)
                                        .Height(48 * R)
                            )
                            .ToArray()
                    )
                );
                rows.Add(Gap(24));
            }
            rows.Add(
                new Button(
                    Loc.AppointmentRate,
                    () =>
                        model.Request(
                            new(
                                AppointmentAction.Rate,
                                Ratings: ratingValues
                                    .Value.Select(pair => new AppointmentRating(
                                        pair.Key,
                                        pair.Value
                                    ))
                                    .ToArray()
                            )
                        )
                )
                    .Enabled(ratingValues.Value.Count > 0)
                    .TextStyle(TextStyleFor("Bold"))
                    .TextAlignment(TextAlignment.Center)
                    .Height(56 * H)
                    .BackgroundColor(Accent)
                    .ForegroundColor(Color.White)
                    .CornerRadius(16 * R)
            );
        }
        else if (!model.State.Working && model.State.Problem == AppointmentProblem.None)
            rows.Add(Label(Loc.RequestFailed));
        rows.Add(Feedback());
        return new VStack(rows.ToArray()).Padding(16 * R);
    }
}
