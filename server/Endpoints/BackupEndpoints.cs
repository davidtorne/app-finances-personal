using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PersonalFinances.Api.Contracts;
using PersonalFinances.Api.Data;

namespace PersonalFinances.Api.Endpoints;

public static partial class BackupEndpoints
{
    private const int MaxBackupsToKeep = 5;

    public static IEndpointRouteBuilder MapBackupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/backups");

        group.MapGet("/", (IWebHostEnvironment environment) =>
        {
            var backups = ListBackupFiles(GetBackupDirectory(environment))
                .Select(ToBackupResultDto)
                .ToList();

            return Results.Ok(backups);
        });

        group.MapPost("/database", async (FinanceDbContext db, IWebHostEnvironment environment) =>
        {
            var databasePath = db.Database.GetDbConnection().DataSource;
            if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
            {
                return Results.NotFound("No s'ha trobat la base de dades.");
            }

            var backupDirectory = GetBackupDirectory(environment);
            Directory.CreateDirectory(backupDirectory);

            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var backupFileName = $"finances-backup-{timestamp}.db";
            var backupPath = Path.Combine(backupDirectory, backupFileName);

            await db.Database.OpenConnectionAsync();
            await using var destination = new SqliteConnection($"Data Source={backupPath}");
            await destination.OpenAsync();

            ((SqliteConnection)db.Database.GetDbConnection()).BackupDatabase(destination);

            TrimOldBackups(backupDirectory);

            return Results.Ok(ToBackupResultDto(new FileInfo(backupPath)));
        });

        group.MapGet("/{fileName}/download", (string fileName, IWebHostEnvironment environment) =>
        {
            var safeFileName = Path.GetFileName(fileName);
            if (!BackupFileNameRegex().IsMatch(safeFileName))
            {
                return Results.BadRequest("Nom de fitxer no vàlid.");
            }

            var backupPath = Path.Combine(GetBackupDirectory(environment), safeFileName);
            if (!File.Exists(backupPath))
            {
                return Results.NotFound("No s'ha trobat la còpia de seguretat.");
            }

            return Results.File(backupPath, "application/octet-stream", safeFileName);
        });

        return app;
    }

    private static string GetBackupDirectory(IWebHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, "data", "backups");

    private static IEnumerable<FileInfo> ListBackupFiles(string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory))
        {
            return [];
        }

        return new DirectoryInfo(backupDirectory)
            .GetFiles("finances-backup-*.db")
            .OrderByDescending(file => file.Name, StringComparer.Ordinal)
            .Take(MaxBackupsToKeep);
    }

    private static void TrimOldBackups(string backupDirectory)
    {
        var filesToDelete = new DirectoryInfo(backupDirectory)
            .GetFiles("finances-backup-*.db")
            .OrderByDescending(file => file.Name, StringComparer.Ordinal)
            .Skip(MaxBackupsToKeep);

        foreach (var file in filesToDelete)
        {
            file.Delete();
        }
    }

    private static BackupResultDto ToBackupResultDto(FileInfo file) => new(
        file.Name,
        Path.Combine("data", "backups", file.Name),
        file.Length,
        file.CreationTime);

    [GeneratedRegex(@"^finances-backup-\d{8}-\d{6}\.db$")]
    private static partial Regex BackupFileNameRegex();
}
