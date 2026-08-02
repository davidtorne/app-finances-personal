using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PersonalFinances.Api.Contracts;
using PersonalFinances.Api.Data;
using PersonalFinances.Api.Services;

namespace PersonalFinances.Api.Endpoints;

public static class BackupEndpoints
{
    public static IEndpointRouteBuilder MapBackupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/backups");

        group.MapPost("/database", async (FinanceDbContext db, IWebHostEnvironment environment, GoogleDriveService drive) =>
        {
            var databasePath = db.Database.GetDbConnection().DataSource;
            if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
            {
                return Results.NotFound("No s'ha trobat la base de dades.");
            }

            var backupDirectory = Path.Combine(environment.ContentRootPath, "data", "backups");
            Directory.CreateDirectory(backupDirectory);

            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var backupFileName = $"finances-backup-{timestamp}.db";
            var backupPath = Path.Combine(backupDirectory, backupFileName);

            await db.Database.OpenConnectionAsync();
            await using var destination = new SqliteConnection($"Data Source={backupPath}");
            await destination.OpenAsync();

            ((SqliteConnection)db.Database.GetDbConnection()).BackupDatabase(destination);

            var info = new FileInfo(backupPath);

            var driveSettings = await drive.GetOrCreateSettingsAsync();
            var driveUploadStatus = "skipped";
            string? driveError = null;

            if (!string.IsNullOrWhiteSpace(driveSettings.RefreshToken) && driveSettings.AutoUpload)
            {
                try
                {
                    await drive.UploadBackupAsync(backupPath, backupFileName);
                    driveUploadStatus = "uploaded";
                }
                catch (Exception ex)
                {
                    driveUploadStatus = "failed";
                    driveError = ex.Message;
                }
            }

            return Results.Ok(new BackupResultDto(
                backupFileName,
                Path.Combine("data", "backups", backupFileName),
                info.Length,
                info.CreationTime,
                driveUploadStatus,
                driveError));
        });

        return app;
    }
}
