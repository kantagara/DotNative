namespace FaliNamIgrac;

public interface IProfileService
{
    Task<bool> IsNicknameAvailableAsync(string nickname, CancellationToken token);
    Task<string> CreateAsync(NewProfile profile, CancellationToken token);
    Task UploadPhotoAsync(string userId, byte[] photo, CancellationToken token);
}
