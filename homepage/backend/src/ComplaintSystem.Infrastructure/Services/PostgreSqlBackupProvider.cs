using System.Diagnostics;
using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ComplaintSystem.Infrastructure.Services;

public sealed class PostgreSqlBackupProvider : IBackupProvider
{
    private readonly BackupOptions _options;
    private readonly NpgsqlConnectionStringBuilder _connection;
    private readonly string _root;

    public PostgreSqlBackupProvider(IOptions<BackupOptions> options, IConfiguration configuration)
    {
        _options = options.Value;
        _connection = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("PostgreSQL connection string is required for backups."));
        _root = Path.GetFullPath(_options.StoragePath);
        Directory.CreateDirectory(_root);
    }

    public async Task<(string StorageReference, long SizeBytes)> CreateAsync(string reference,
        CancellationToken cancellationToken)
    {
        var output = ResolvePath($"{reference}.dump");
        var startInfo = CreateStartInfo(_options.Command ?? "pg_dump");
        startInfo.ArgumentList.Add("--format=custom");
        startInfo.ArgumentList.Add("--no-owner");
        startInfo.ArgumentList.Add("--file");
        startInfo.ArgumentList.Add(output);
        AddConnectionArguments(startInfo);
        await RunAsync(startInfo, cancellationToken);
        var file = new FileInfo(output);
        if (!file.Exists || file.Length == 0) throw new InvalidOperationException("The database backup did not produce an output file.");
        return (file.FullName, file.Length);
    }

    public async Task RestoreAsync(string storageReference, CancellationToken cancellationToken)
    {
        var input = ResolvePath(Path.GetFileName(storageReference));
        if (!File.Exists(input)) throw new FileNotFoundException("The backup file no longer exists.", input);
        var startInfo = CreateStartInfo(_options.RestoreCommand ?? "pg_restore");
        startInfo.ArgumentList.Add("--clean");
        startInfo.ArgumentList.Add("--if-exists");
        startInfo.ArgumentList.Add("--no-owner");
        startInfo.ArgumentList.Add("--dbname");
        startInfo.ArgumentList.Add(_connection.Database!);
        AddConnectionArguments(startInfo, includeDatabase: false);
        startInfo.ArgumentList.Add(input);
        await RunAsync(startInfo, cancellationToken);
    }

    private ProcessStartInfo CreateStartInfo(string executable)
    {
        var info = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        if (!string.IsNullOrEmpty(_connection.Password)) info.Environment["PGPASSWORD"] = _connection.Password;
        return info;
    }

    private void AddConnectionArguments(ProcessStartInfo info, bool includeDatabase = true)
    {
        info.ArgumentList.Add("--host"); info.ArgumentList.Add(_connection.Host!);
        info.ArgumentList.Add("--port"); info.ArgumentList.Add(_connection.Port.ToString());
        info.ArgumentList.Add("--username"); info.ArgumentList.Add(_connection.Username!);
        if (includeDatabase)
        {
            info.ArgumentList.Add("--dbname"); info.ArgumentList.Add(_connection.Database!);
        }
    }

    private static async Task RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The database backup process could not be started.");
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        _ = process.StandardOutput.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw;
        }
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"The database utility failed with exit code {process.ExitCode}: {error[..Math.Min(error.Length, 500)]}");
        }
    }

    private string ResolvePath(string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(_root, fileName));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Backup path escaped its configured root.");
        return path;
    }
}
