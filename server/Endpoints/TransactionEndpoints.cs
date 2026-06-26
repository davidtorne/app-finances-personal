using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PersonalFinances.Api.Contracts;
using PersonalFinances.Api.Data;
using PersonalFinances.Api.Models;
using System.Data.Common;

namespace PersonalFinances.Api.Endpoints;

public static class TransactionEndpoints
{
    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/transactions");

        group.MapGet("/", async (
            DateOnly? from,
            DateOnly? to,
            int[]? tagIds,
            FinanceDbContext db) =>
        {
            var query = db.Transactions
                .AsNoTracking()
                .Include(item => item.TransactionTags)
                .ThenInclude(item => item.Tag)
                .ThenInclude(item => item.TagGroup)
                .AsQueryable();

            if (from.HasValue)
            {
                query = query.Where(item => item.Date >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(item => item.Date <= to.Value);
            }

            if (tagIds is { Length: > 0 })
            {
                foreach (var tagId in tagIds.Distinct())
                {
                    query = query.Where(item =>
                        item.TransactionTags.Any(link => link.TagId == tagId));
                }
            }

            var transactions = await query
                .OrderByDescending(item => item.Date)
                .ThenByDescending(item => item.Id)
                .ToListAsync();

            return transactions.Select(MapTransaction);
        });

        group.MapPost("/", async (
            CreateTransactionRequest request,
            FinanceDbContext db) =>
        {
            var validation = ValidateRequest(request);
            if (validation is not null)
            {
                return Results.BadRequest(validation);
            }

            Enum.TryParse<TransactionType>(request.Type, true, out var type);
            var transactionId = await InsertTransactionAsync(
                db,
                type,
                decimal.Round(request.Amount, 2),
                request.Date,
                request.Description.Trim(),
                request.TagIds.Distinct().ToArray());

            return Results.Created($"/api/transactions/{transactionId}", new { id = transactionId });
        });

        group.MapDelete("/{id:int}", async (int id, FinanceDbContext db) =>
        {
            var transaction = await db.Transactions.FindAsync(id);
            if (transaction is null)
            {
                return Results.NotFound();
            }

            db.Transactions.Remove(transaction);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return app;
    }

    private static string? ValidateRequest(CreateTransactionRequest request)
    {
        if (!Enum.TryParse<TransactionType>(request.Type, true, out _))
        {
            return "El tipus ha de ser 'income' o 'expense'.";
        }

        if (request.Amount <= 0)
        {
            return "L'import ha de ser superior a zero.";
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return "La descripcio es obligatoria.";
        }

        return null;
    }

    private static async Task<long> InsertTransactionAsync(
        FinanceDbContext db,
        TransactionType type,
        decimal amount,
        DateOnly date,
        string description,
        int[] tagIds)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();

        await using var transaction = await db.Database.BeginTransactionAsync();
        await using var insertTransaction = connection.CreateCommand();
        insertTransaction.Transaction = transaction.GetDbTransaction();
        insertTransaction.CommandText = """
            INSERT INTO Transactions (Type, Amount, Date, Description, CreatedAtUtc)
            VALUES ($type, $amount, $date, $description, $createdAtUtc);
            SELECT last_insert_rowid();
            """;

        AddParameter(insertTransaction, "$type", type.ToString());
        AddParameter(insertTransaction, "$amount", amount);
        AddParameter(insertTransaction, "$date", date.ToString("yyyy-MM-dd"));
        AddParameter(insertTransaction, "$description", description);
        AddParameter(insertTransaction, "$createdAtUtc", DateTime.UtcNow.ToString("O"));

        var transactionId = (long)(await insertTransaction.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("No s'ha pogut crear la transaccio."));

        if (tagIds.Length > 0)
        {
            await using var insertTag = connection.CreateCommand();
            insertTag.Transaction = transaction.GetDbTransaction();
            insertTag.CommandText = """
                INSERT OR IGNORE INTO TransactionTags (TransactionId, TagId)
                VALUES ($transactionId, $tagId);
                """;

            var transactionIdParameter = insertTag.CreateParameter();
            transactionIdParameter.ParameterName = "$transactionId";
            transactionIdParameter.Value = transactionId;
            insertTag.Parameters.Add(transactionIdParameter);

            var tagIdParameter = insertTag.CreateParameter();
            tagIdParameter.ParameterName = "$tagId";
            insertTag.Parameters.Add(tagIdParameter);

            foreach (var tagId in tagIds)
            {
                tagIdParameter.Value = tagId;
                await insertTag.ExecuteNonQueryAsync();
            }
        }

        await transaction.CommitAsync();
        return transactionId;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static TransactionDto MapTransaction(FinanceTransaction item) =>
        new(
            item.Id,
            item.Type.ToString().ToLowerInvariant(),
            item.Amount,
            item.Date,
            item.Description,
            item.TransactionTags
                .OrderBy(link => link.Tag.TagGroup.Name)
                .ThenBy(link => link.Tag.Name)
                .Select(link => new TransactionTagDto(
                    link.Tag.Id,
                    link.Tag.TagGroupId,
                    link.Tag.TagGroup.Name,
                    link.Tag.Name,
                    link.Tag.Color))
                .ToArray());
}
