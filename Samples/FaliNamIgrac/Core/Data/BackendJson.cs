using System.Text.Json.Serialization;

namespace FaliNamIgrac;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    NumberHandling = JsonNumberHandling.AllowReadingFromString
)]
[JsonSerializable(typeof(AccountResponse))]
[JsonSerializable(typeof(AccountResponse[]))]
[JsonSerializable(typeof(UpdateAccountRequest))]
[JsonSerializable(typeof(ChatSocketAuth))]
[JsonSerializable(typeof(ChatSocketFrame))]
[JsonSerializable(typeof(ChatGraphRequest))]
[JsonSerializable(typeof(ChatGraphResponse))]
[JsonSerializable(typeof(ChatReadRequest))]
[JsonSerializable(typeof(ChatNotificationRequest))]
[JsonSerializable(typeof(ChatUser[]))]
[JsonSerializable(typeof(ChatUsersQuery))]
[JsonSerializable(typeof(ChatSearchQuery))]
[JsonSerializable(typeof(GamesQuery))]
[JsonSerializable(typeof(MyGamesQuery))]
[JsonSerializable(typeof(ProfileQuery))]
[JsonSerializable(typeof(EmailQuery))]
[JsonSerializable(typeof(NicknameQuery))]
[JsonSerializable(typeof(PlacesAutocompleteQuery))]
[JsonSerializable(typeof(PlacesDetailsQuery))]
[JsonSerializable(typeof(GameResponse[]))]
[JsonSerializable(typeof(GameDetailsResponse))]
[JsonSerializable(typeof(ProfileResponse))]
[JsonSerializable(typeof(TemporaryUserResponse))]
[JsonSerializable(typeof(CreatedProfileResponse))]
[JsonSerializable(typeof(PlacesAutocompleteResponse))]
[JsonSerializable(typeof(PlacesDetailsResponse))]
[JsonSerializable(typeof(CognitoIdentity[]))]
[JsonSerializable(typeof(GameApplicationRequest))]
[JsonSerializable(typeof(TemporaryUserRequest))]
[JsonSerializable(typeof(NewProfile))]
[JsonSerializable(typeof(ProfilePictureUpdate))]
[JsonSerializable(typeof(string))]
internal partial class BackendJson : JsonSerializerContext;
