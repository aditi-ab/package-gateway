namespace PackageGateway.Storage;

public sealed class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    public string Path { get; set; } = "/data/blobs";
}