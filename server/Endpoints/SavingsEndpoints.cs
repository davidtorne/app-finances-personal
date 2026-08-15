using Microsoft.EntityFrameworkCore;
using PersonalFinances.Api.Contracts;
using PersonalFinances.Api.Data;
using PersonalFinances.Api.Models;

namespace PersonalFinances.Api.Endpoints;

public static class SavingsEndpoints
{
    public static IEndpointRouteBuilder MapSavingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/savings");

        group.MapGet("/accounts", async (int? year, FinanceDbContext db) =>
        {
            var accounts = await db.SavingsAccounts
                .AsNoTracking()
                .Include(item => item.Movements)
                .OrderBy(item => item.Name)
                .ToListAsync();

            var linkedTransactions = await GetLinkedTransactionsAsync(db);

            return accounts.Select(account => MapSavingsAccount(account, linkedTransactions, year));
        });

        group.MapPost("/accounts", async (CreateSavingsAccountRequest request, FinanceDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("Indica el nom del compte.");
            }

            var account = new SavingsAccount
            {
                Name = request.Name.Trim(),
                Color = string.IsNullOrWhiteSpace(request.Color) ? "#2563eb" : request.Color,
            };
            db.SavingsAccounts.Add(account);
            await db.SaveChangesAsync();
            return Results.Created($"/api/savings/accounts/{account.Id}", new { id = account.Id });
        });

        group.MapDelete("/accounts/{id:int}", async (int id, FinanceDbContext db) =>
        {
            var account = await db.SavingsAccounts.FindAsync(id);
            if (account is null)
            {
                return Results.NotFound();
            }

            db.SavingsAccounts.Remove(account);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapGet("/movements", async (int? accountId, int? year, FinanceDbContext db) =>
        {
            var manualQuery = db.SavingsMovements
                .AsNoTracking()
                .Include(item => item.SavingsAccount)
                .AsQueryable();
            if (accountId is > 0)
            {
                manualQuery = manualQuery.Where(item => item.SavingsAccountId == accountId);
            }

            if (year is > 0)
            {
                var yearFrom = new DateOnly(year.Value, 1, 1);
                var yearTo = new DateOnly(year.Value, 12, 31);
                manualQuery = manualQuery.Where(item => item.Date >= yearFrom && item.Date <= yearTo);
            }

            var manualMovements = await manualQuery.ToListAsync();
            var linkedTransactions = await GetLinkedTransactionsAsync(db);
            var accountsById = await db.SavingsAccounts.AsNoTracking().ToDictionaryAsync(item => item.Id);

            var movements = manualMovements
                .Select(item => MapSavingsMovement(item, "manual"))
                .Concat(linkedTransactions
                    .Where(transaction => year is not > 0 || transaction.Date.Year == year)
                    .SelectMany(transaction => LinkedSavingsAccountIds(transaction)
                        .Where(linkedAccountId => accountId is not > 0 || linkedAccountId == accountId)
                        .Where(accountsById.ContainsKey)
                        .Select(linkedAccountId => MapTransactionAsMovement(transaction, accountsById[linkedAccountId])))
                    )
                .OrderByDescending(item => item.Date)
                .ThenByDescending(item => item.Id)
                .ToList();

            return movements;
        });

        group.MapPost("/movements", async (CreateSavingsMovementRequest request, FinanceDbContext db) =>
        {
            var validation = await ValidateMovementAsync(request, db);
            if (validation is not null)
            {
                return Results.BadRequest(validation);
            }

            Enum.TryParse<SavingsMovementType>(request.Type, true, out var type);
            var movement = new SavingsMovement
            {
                SavingsAccountId = request.SavingsAccountId,
                Type = type,
                Amount = decimal.Round(request.Amount, 2),
                Date = request.Date,
                Description = request.Description.Trim(),
            };
            db.SavingsMovements.Add(movement);
            await db.SaveChangesAsync();
            return Results.Created($"/api/savings/movements/{movement.Id}", new { id = movement.Id });
        });

        group.MapDelete("/movements/{id:int}", async (int id, FinanceDbContext db) =>
        {
            var movement = await db.SavingsMovements.FindAsync(id);
            if (movement is null)
            {
                return Results.NotFound();
            }

            db.SavingsMovements.Remove(movement);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return app;
    }

    private static async Task<List<FinanceTransaction>> GetLinkedTransactionsAsync(FinanceDbContext db) =>
        await db.Transactions
            .AsNoTracking()
            .Include(item => item.TransactionTags)
            .ThenInclude(item => item.Tag)
            .Where(item => item.TransactionTags.Any(tt => tt.Tag.LinkedSavingsAccountId != null))
            .ToListAsync();

    private static IEnumerable<int> LinkedSavingsAccountIds(FinanceTransaction transaction) =>
        transaction.TransactionTags
            .Select(item => item.Tag.LinkedSavingsAccountId)
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .Distinct();

    private static async Task<string?> ValidateMovementAsync(CreateSavingsMovementRequest request, FinanceDbContext db)
    {
        if (string.IsNullOrWhiteSpace(request.Description) || request.Amount <= 0)
        {
            return "Indica un concepte i un import superior a zero.";
        }

        if (!Enum.TryParse<SavingsMovementType>(request.Type, true, out _))
        {
            return "El tipus ha de ser 'deposit' o 'withdrawal'.";
        }

        var accountExists = await db.SavingsAccounts.AnyAsync(item => item.Id == request.SavingsAccountId);
        if (!accountExists)
        {
            return "El compte d'estalvi no existeix.";
        }

        return null;
    }

    private static SavingsAccountDto MapSavingsAccount(SavingsAccount account, IReadOnlyCollection<FinanceTransaction> linkedTransactions, int? year)
    {
        var movements = year is > 0
            ? account.Movements.Where(item => item.Date.Year == year)
            : account.Movements;
        var manualDeposits = movements
            .Where(item => item.Type == SavingsMovementType.Deposit)
            .Sum(item => item.Amount);
        var manualWithdrawals = movements
            .Where(item => item.Type == SavingsMovementType.Withdrawal)
            .Sum(item => item.Amount);

        var linkedForAccount = linkedTransactions
            .Where(transaction => LinkedSavingsAccountIds(transaction).Contains(account.Id))
            .Where(transaction => year is not > 0 || transaction.Date.Year == year)
            .ToArray();
        var autoDeposits = linkedForAccount
            .Where(item => item.Type == TransactionType.Expense)
            .Sum(item => item.Amount);
        var autoWithdrawals = linkedForAccount
            .Where(item => item.Type == TransactionType.Income)
            .Sum(item => item.Amount);

        var deposits = manualDeposits + autoDeposits;
        var withdrawals = manualWithdrawals + autoWithdrawals;

        return new SavingsAccountDto(
            account.Id,
            account.Name,
            account.Color,
            deposits - withdrawals,
            deposits,
            withdrawals);
    }

    private static SavingsMovementDto MapSavingsMovement(SavingsMovement movement, string source) =>
        new(
            movement.Id,
            movement.SavingsAccountId,
            movement.SavingsAccount.Name,
            movement.Type.ToString().ToLowerInvariant(),
            movement.Amount,
            movement.Date,
            movement.Description,
            source);

    private static SavingsMovementDto MapTransactionAsMovement(FinanceTransaction transaction, SavingsAccount account) =>
        new(
            transaction.Id,
            account.Id,
            account.Name,
            transaction.Type == TransactionType.Expense ? "deposit" : "withdrawal",
            transaction.Amount,
            transaction.Date,
            transaction.Description,
            "transaction");
}
