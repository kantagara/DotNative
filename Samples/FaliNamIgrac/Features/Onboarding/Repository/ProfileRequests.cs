namespace FaliNamIgrac;

public sealed record ProfileSport(
    string SportName,
    int SkillLevel = 3,
    string NotificationType = "ON"
);

public sealed record NewProfile(
    int TempUserId,
    string Username,
    string Nickname,
    string Firstname,
    string Lastname,
    string Bio,
    string? BirthDate,
    string? Gender,
    string? PhoneNumber,
    ProfileSport[] Sports,
    double LocationLat,
    double LocationLong,
    string City,
    string Address,
    string ProfileImageUrl
);

internal sealed record ProfilePictureUpdate(int UserId, string Extension);
