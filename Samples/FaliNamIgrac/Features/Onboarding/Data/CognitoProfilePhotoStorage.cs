namespace FaliNamIgrac;

public sealed class CognitoProfilePhotoStorage(
    ISessionService session,
    ProfilePhotoConfiguration? configuration = null
) : IProfilePhotoStorage
{
    public async Task UploadAsync(string userId, byte[] photo, CancellationToken token)
    {
        var format = PhotoFormat.Detect(photo);
        var config = configuration ?? ProfilePhotoConfiguration.FromEnvironment();
        var region = Amazon.RegionEndpoint.GetBySystemName(config.Region);
        using var credentials = new Amazon.CognitoIdentity.CognitoAWSCredentials(
            config.IdentityPoolId,
            region
        );
        credentials.AddLogin(
            "cognito-idp."
                + BackendConfiguration.Region
                + ".amazonaws.com/"
                + BackendConfiguration.PoolId,
            await session.IdTokenAsync(token)
        );
        using var storage = new Amazon.S3.AmazonS3Client(credentials, region);
        using var stream = new MemoryStream(photo);
        await storage.PutObjectAsync(
            new Amazon.S3.Model.PutObjectRequest
            {
                BucketName = config.Bucket,
                Key = "public/" + userId + format.Extension,
                ContentType = format.ContentType,
                InputStream = stream,
            },
            token
        );
    }
}

public sealed record ProfilePhotoConfiguration(string IdentityPoolId, string Region, string Bucket)
{
    public static ProfilePhotoConfiguration FromEnvironment()
    {
        static string Required(string key) =>
            PublicConfiguration.Get(key) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"Missing {key}.");
        return new(
            Required("FNI_PROFILE_IDENTITY_POOL_ID"),
            Required("FNI_PROFILE_STORAGE_REGION"),
            Required("FNI_PROFILE_IMAGE_BUCKET")
        );
    }
}
