namespace FaliNamIgrac;

public sealed class ProfileService(IApiClient backend, ISessionService session) : IProfileService
{
    private readonly IProfilePhotoStorage photos = new CognitoProfilePhotoStorage(session);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(100);

    public async Task<bool> IsNicknameAvailableAsync(string nickname, CancellationToken token)
    {
        using var response = await backend.GetResponseAsync(
            "user-data/check-nickname",
            new NicknameQuery(nickname),
            token,
            authenticated: true,
            timeout: RequestTimeout
        );
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<string> CreateAsync(NewProfile profile, CancellationToken token)
    {
        using var response = await backend.PostJsonAsync(
            "user-data/add-user",
            profile,
            token,
            authenticated: true,
            timeout: RequestTimeout
        );
        response.EnsureSuccessStatusCode();
        return (await ApiJson.ReadAsync<CreatedProfileResponse>(response.Content, token)).UserId;
    }

    public async Task UploadPhotoAsync(string userId, byte[] photo, CancellationToken token)
    {
        await photos.UploadAsync(userId, photo, token);
        using var response = await backend.PutJsonAsync(
            "user-data/update-profile-picture",
            new ProfilePictureUpdate(int.Parse(userId), PhotoFormat.Detect(photo).Extension),
            token,
            authenticated: true,
            timeout: RequestTimeout
        );
        response.EnsureSuccessStatusCode();
    }
}
