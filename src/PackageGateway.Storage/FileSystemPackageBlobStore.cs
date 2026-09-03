using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PackageGateway.Application;
using PackageGateway.Domain;

namespace PackageGateway.Storage;

public sealed class FileSystemPackageBlobStore(
    GatewayDbContext db,
    IOptions<BlobStorageOptions> options) : IPackageBlobStore, ILegacyPackageBlobMigrator
{
    private readonly string root = EnsureRoot(options.Value.Path);

    public async Task<int> MigrateBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        var blobs = await db.PackageBlobs.OrderBy(x => x.PackageVersionId).Take(Math.Clamp(batchSize, 1, 10))
            .ToListAsync(cancellationToken);
        foreach (var blob in blobs)
        {
            await StoreBytesAsync(blob.Content, blob.Sha256, cancellationToken);
            db.PackageBlobs.Remove(blob);
        }

        if (blobs.Count > 0) await db.SaveChangesAsync(cancellationToken);
        return blobs.Count;
    }

    public async Task<Stream?> OpenReadAsync(Guid packageVersionId, CancellationToken cancellationToken)
    {
        var sha256 = await db.PackageVersions.AsNoTracking().Where(x => x.Id == packageVersionId)
            .Select(x => x.Sha256).SingleOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(sha256))
        {
            var path = BlobPath(sha256);
            if (File.Exists(path)) return OpenRead(path);
        }

        var legacy = await db.PackageBlobs.SingleOrDefaultAsync(x => x.PackageVersionId == packageVersionId,
            cancellationToken);
        if (legacy is null) return null;
        await StoreBytesAsync(legacy.Content, legacy.Sha256, cancellationToken);
        return OpenRead(BlobPath(legacy.Sha256));
    }

    public async Task StoreAsync(Guid packageVersionId, Stream content, string sha256, long maximumBytes,
        CancellationToken cancellationToken)
    {
        ValidateSha256(sha256);
        var destination = BlobPath(sha256);
        if (File.Exists(destination)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".{Guid.CreateVersion7():N}.tmp");
        try
        {
            await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > maximumBytes)
                    throw new InvalidDataException("Artifact exceeds configured maximum size.");
                hash.AppendData(buffer.AsSpan(0, read));
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await output.FlushAsync(cancellationToken);
            var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (!StringComparer.OrdinalIgnoreCase.Equals(actual, sha256))
                throw new InvalidDataException("Artifact content does not match its SHA-256 digest.");
            output.Close();
            MoveIntoPlace(temporary, destination);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public async Task<bool> DeleteUnapprovedAsync(Guid packageVersionId, CancellationToken cancellationToken)
    {
        var version = await db.PackageVersions.SingleOrDefaultAsync(x => x.Id == packageVersionId, cancellationToken);
        if (version is null || version.Status == PackageVersionStatus.Approved) return false;
        var exists = await db.PackageBlobs.AnyAsync(x => x.PackageVersionId == packageVersionId, cancellationToken) ||
                     (!string.IsNullOrWhiteSpace(version.Sha256) && File.Exists(BlobPath(version.Sha256)));
        if (!exists) return false;
        await DeleteAsync(packageVersionId, version.Sha256, cancellationToken);
        return true;
    }

    public async Task DeleteAsync(Guid packageVersionId, string? sha256, CancellationToken cancellationToken)
    {
        var legacy = await db.PackageBlobs.SingleOrDefaultAsync(x => x.PackageVersionId == packageVersionId,
            cancellationToken);
        if (legacy is not null) db.PackageBlobs.Remove(legacy);
        if (string.IsNullOrWhiteSpace(sha256)) return;
        var shared = await db.PackageVersions.AsNoTracking().AnyAsync(
            x => x.Id != packageVersionId && x.Sha256 == sha256, cancellationToken);
        if (!shared) File.Delete(BlobPath(sha256));
    }

    private async Task StoreBytesAsync(byte[] content, string sha256, CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(content, false);
        await StoreAsync(Guid.Empty, stream, sha256, content.LongLength, cancellationToken);
    }

    private string BlobPath(string sha256)
    {
        ValidateSha256(sha256);
        var normalized = sha256.ToLowerInvariant();
        return Path.Combine(root, "sha256", normalized[..2], normalized[2..4], normalized);
    }

    private static FileStream OpenRead(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    private static void MoveIntoPlace(string temporary, string destination)
    {
        try
        {
            File.Move(temporary, destination);
        }
        catch (IOException) when (File.Exists(destination))
        {
            // Another acquisition stored the same content-addressed blob first.
        }
    }

    private static void ValidateSha256(string sha256)
    {
        if (sha256.Length != 64 || sha256.Any(x => !Uri.IsHexDigit(x)))
            throw new ArgumentException("A 64-character SHA-256 digest is required.", nameof(sha256));
    }

    private static string EnsureRoot(string configuredPath)
    {
        var path = Path.GetFullPath(configuredPath);
        Directory.CreateDirectory(path);
        return path;
    }
}