namespace FaliNamIgrac;

public interface IGroupPhotoStorage
{
    Task<string> UploadAsync(string groupId, byte[] photo, CancellationToken token);
}

public sealed class GroupPhotoStorage(ISessionService session) : IGroupPhotoStorage
{
    public async Task<string> UploadAsync(string groupId, byte[] photo, CancellationToken token)
    {
        var format = PhotoFormat.Detect(photo);
        // A stable key makes a retry of the same form overwrite its own upload.
        var key = "public/group-photos/" + Uri.EscapeDataString(groupId) + format.Extension;
        var config = ProfilePhotoConfiguration.FromEnvironment();
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
                Key = key,
                ContentType = format.ContentType,
                InputStream = stream,
            },
            token
        );
        return key;
    }
}
