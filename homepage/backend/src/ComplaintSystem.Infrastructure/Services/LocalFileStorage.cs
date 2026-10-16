using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ComplaintSystem.Infrastructure.Services;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IOptions<EvidenceOptions> options)
    {
        _root = Path.GetFullPath(options.Value.StoragePath);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(string generatedFileName, Stream content, CancellationToken cancellationToken)
    {
        if (Path.GetFileName(generatedFileName) != generatedFileName)
        {
            throw new InvalidOperationException("Invalid generated file name.");
        }
        var path = ResolvePath(generatedFileName);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await content.CopyToAsync(output, cancellationToken);
        return path;
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(Path.GetFileName(storagePath));
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(ResolvePath(Path.GetFileName(storagePath)));
        return Task.CompletedTask;
    }

    private string ResolvePath(string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(_root, fileName));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage path escaped its configured root.");
        }
        return path;
    }
}
