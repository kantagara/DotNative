namespace FaliNamIgrac;

public interface IProfilePhotoStorage
{
    Task UploadAsync(string userId, byte[] photo, CancellationToken token);
}
