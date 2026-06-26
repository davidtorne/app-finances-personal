using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PersonalFinances.Api.Data;

namespace PersonalFinances.Api.Endpoints;

public static class BackupEndpoints
{
    public static IEndpointRouteBuilder MapBackupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/backups");

        group.MapPost("/database", async (FinanceDbContext db, IWebHostEnvironment environment) =>
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
            return Results.Ok(new
            {
                fileName = backupFileName,
                relativePath = Path.Combine("data", "backups", backupFileName),
                sizeBytes = info.Length,
                createdAt = info.CreationTime,
            });
        });

        return app;
    }
}
