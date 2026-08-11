using Microsoft.EntityFrameworkCore;
using System.Data;

namespace PersonalFinances.Api.Data;

public static class DatabaseSchema
{
    public static async Task EnsureBudgetTablesAsync(FinanceDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS Budgets (
                Id INTEGER NOT NULL CONSTRAINT PK_Budgets PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                "From" TEXT NOT NULL,
                "To" TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS BudgetItems (
                Id INTEGER NOT NULL CONSTRAINT PK_BudgetItems PRIMARY KEY AUTOINCREMENT,
                BudgetId INTEGER NOT NULL,
                Description TEXT NOT NULL,
                Type TEXT NOT NULL DEFAULT 'Expense',
                ExpectedAmount TEXT NOT NULL,
                CONSTRAINT FK_BudgetItems_Budgets_BudgetId FOREIGN KEY (BudgetId) REFERENCES Budgets (Id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS BudgetItemTags (
                BudgetItemId INTEGER NOT NULL,
                TagId INTEGER NOT NULL,
                CONSTRAINT PK_BudgetItemTags PRIMARY KEY (BudgetItemId, TagId),
                CONSTRAINT FK_BudgetItemTags_BudgetItems_BudgetItemId FOREIGN KEY (BudgetItemId) REFERENCES BudgetItems (Id) ON DELETE CASCADE,
                CONSTRAINT FK_BudgetItemTags_Tags_TagId FOREIGN KEY (TagId) REFERENCES Tags (Id) ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS IX_BudgetItems_BudgetId ON BudgetItems (BudgetId);
            CREATE INDEX IF NOT EXISTS IX_BudgetItemTags_TagId ON BudgetItemTags (TagId);
            """);

        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var checkColumn = connection.CreateCommand();
        checkColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('BudgetItems') WHERE name = 'Type';";
        var hasTypeColumn = Convert.ToInt32(await checkColumn.ExecuteScalarAsync()) > 0;
        if (!hasTypeColumn)
        {
            await using var addColumn = connection.CreateCommand();
            addColumn.CommandText = "ALTER TABLE BudgetItems ADD COLUMN Type TEXT NOT NULL DEFAULT 'Expense';";
            await addColumn.ExecuteNonQueryAsync();
        }
    }

    public static async Task EnsureFixedExpenseTablesAsync(FinanceDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS FixedExpenses (
                Id INTEGER NOT NULL CONSTRAINT PK_FixedExpenses PRIMARY KEY AUTOINCREMENT,
                Type TEXT NOT NULL DEFAULT 'Expense',
                Description TEXT NOT NULL,
                Amount TEXT NOT NULL,
                Month INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS FixedExpenseTags (
                FixedExpenseId INTEGER NOT NULL,
                TagId INTEGER NOT NULL,
                CONSTRAINT PK_FixedExpenseTags PRIMARY KEY (FixedExpenseId, TagId),
                CONSTRAINT FK_FixedExpenseTags_FixedExpenses_FixedExpenseId FOREIGN KEY (FixedExpenseId) REFERENCES FixedExpenses (Id) ON DELETE CASCADE,
                CONSTRAINT FK_FixedExpenseTags_Tags_TagId FOREIGN KEY (TagId) REFERENCES Tags (Id) ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS IX_FixedExpenseTags_TagId ON FixedExpenseTags (TagId);
            """);

        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var checkColumn = connection.CreateCommand();
        checkColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('FixedExpenses') WHERE name = 'ForecastWeek';";
        var hasForecastWeekColumn = Convert.ToInt32(await checkColumn.ExecuteScalarAsync()) > 0;
        if (!hasForecastWeekColumn)
        {
            await using var addColumn = connection.CreateCommand();
            addColumn.CommandText = "ALTER TABLE FixedExpenses ADD COLUMN ForecastWeek INTEGER NULL;";
            await addColumn.ExecuteNonQueryAsync();
        }
    }

    public static async Task EnsureDriveSettingsTableAsync(FinanceDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS DriveSettings (
                Id INTEGER NOT NULL CONSTRAINT PK_DriveSettings PRIMARY KEY,
                ClientId TEXT NULL,
                ClientSecret TEXT NULL,
                RefreshToken TEXT NULL,
                ConnectedAccountEmail TEXT NULL,
                FolderId TEXT NULL,
                AutoUpload INTEGER NOT NULL DEFAULT 1
            );
            """);
    }

    public static async Task EnsureMonthlyFixedExpenseTablesAsync(FinanceDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            DROP TABLE IF EXISTS ForecastExcludedTags;
            DROP TABLE IF EXISTS ForecastCategoryAssignments;
            CREATE TABLE IF NOT EXISTS MonthlyFixedExpenses (
                Id INTEGER NOT NULL CONSTRAINT PK_MonthlyFixedExpenses PRIMARY KEY AUTOINCREMENT,
                Description TEXT NOT NULL,
                Amount TEXT NOT NULL,
                Week INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL
            );
            """);
    }
}
