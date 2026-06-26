using Microsoft.EntityFrameworkCore;
using PersonalFinances.Api.Contracts;
using PersonalFinances.Api.Data;
using PersonalFinances.Api.Models;

namespace PersonalFinances.Api.Endpoints;

public static class BudgetEndpoints
{
    public static IEndpointRouteBuilder MapBudgetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/budgets");

        group.MapGet("/", async (FinanceDbContext db) =>
        {
            var budgets = await db.Budgets
                .AsNoTracking()
                .Include(item => item.Items)
                .ThenInclude(item => item.BudgetItemTags)
                .ThenInclude(item => item.Tag)
                .ThenInclude(item => item.TagGroup)
                .OrderByDescending(item => item.From)
                .ThenByDescending(item => item.Id)
                .ToListAsync();

            if (budgets.Count == 0)
            {
                return [];
            }

            var firstDate = budgets.Min(item => item.From);
            var lastDate = budgets.Max(item => item.To);
            var transactions = await db.Transactions
                .AsNoTracking()
                .Include(item => item.TransactionTags)
                .ThenInclude(item => item.Tag)
                .ThenInclude(item => item.TagGroup)
                .Where(item => item.Date >= firstDate && item.Date <= lastDate)
                .ToListAsync();

            return budgets.Select(budget => MapBudget(budget, transactions));
        });

        group.MapPost("/", async (CreateBudgetRequest request, FinanceDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("El nom del pressupost és obligatori.");
            }

            if (request.To < request.From)
            {
                return Results.BadRequest("La data final no pot ser anterior a la inicial.");
            }

            var budget = new Budget
            {
                Name = request.Name.Trim(),
                From = request.From,
                To = request.To
            };
            db.Budgets.Add(budget);
            await db.SaveChangesAsync();
            return Results.Created($"/api/budgets/{budget.Id}", new { id = budget.Id });
        });

        group.MapPost("/{budgetId:int}/copy", async (
            int budgetId,
            CreateBudgetRequest request,
            FinanceDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("El nom del pressupost és obligatori.");
            }

            if (request.To < request.From)
            {
                return Results.BadRequest("La data final no pot ser anterior a la inicial.");
            }

            var source = await db.Budgets
                .AsNoTracking()
                .Include(item => item.Items)
                .ThenInclude(item => item.BudgetItemTags)
                .FirstOrDefaultAsync(item => item.Id == budgetId);
            if (source is null)
            {
                return Results.NotFound("El pressupost d'origen no existeix.");
            }

            var copy = new Budget
            {
                Name = request.Name.Trim(),
                From = request.From,
                To = request.To,
                Items = source.Items.Select(item => new BudgetItem
                {
                    Type = item.Type,
                    Description = item.Description,
                    ExpectedAmount = item.ExpectedAmount,
                    BudgetItemTags = item.BudgetItemTags
                        .Select(link => new BudgetItemTag { TagId = link.TagId })
                        .ToArray()
                }).ToArray()
            };

            db.Budgets.Add(copy);
            await db.SaveChangesAsync();
            return Results.Created($"/api/budgets/{copy.Id}", new { id = copy.Id });
        });

        group.MapPost("/{budgetId:int}/items", async (
            int budgetId,
            CreateBudgetItemRequest request,
            FinanceDbContext db) =>
        {
            if (!await db.Budgets.AnyAsync(item => item.Id == budgetId))
            {
                return Results.NotFound("El pressupost no existeix.");
            }

            if (string.IsNullOrWhiteSpace(request.Description) || request.ExpectedAmount <= 0)
            {
                return Results.BadRequest("Indica un concepte i un import previst superior a zero.");
            }

            if (!Enum.TryParse<TransactionType>(request.Type, true, out var type))
            {
                return Results.BadRequest("El tipus ha de ser 'income' o 'expense'.");
            }

            var tagIds = request.TagIds.Distinct().ToArray();
            var existingTagIds = await db.Tags
                .Where(tag => tagIds.Contains(tag.Id))
                .Select(tag => tag.Id)
                .ToArrayAsync();
            if (existingTagIds.Length != tagIds.Length)
            {
                return Results.BadRequest("Un o més tags no existeixen.");
            }

            var budgetItem = new BudgetItem
            {
                BudgetId = budgetId,
                Description = request.Description.Trim(),
                Type = type,
                ExpectedAmount = decimal.Round(request.ExpectedAmount, 2),
                BudgetItemTags = tagIds
                    .Select(tagId => new BudgetItemTag { TagId = tagId })
                    .ToArray()
            };
            db.BudgetItems.Add(budgetItem);
            await db.SaveChangesAsync();
            return Results.Created($"/api/budgets/{budgetId}/items/{budgetItem.Id}", new { id = budgetItem.Id });
        });

        group.MapPut("/{budgetId:int}/items/{itemId:int}", async (
            int budgetId,
            int itemId,
            CreateBudgetItemRequest request,
            FinanceDbContext db) =>
        {
            var budgetItem = await db.BudgetItems
                .Include(item => item.BudgetItemTags)
                .FirstOrDefaultAsync(item => item.Id == itemId && item.BudgetId == budgetId);
            if (budgetItem is null)
            {
                return Results.NotFound("El concepte no existeix.");
            }

            if (string.IsNullOrWhiteSpace(request.Description) || request.ExpectedAmount <= 0)
            {
                return Results.BadRequest("Indica un concepte i un import previst superior a zero.");
            }

            if (!Enum.TryParse<TransactionType>(request.Type, true, out var type))
            {
                return Results.BadRequest("El tipus ha de ser 'income' o 'expense'.");
            }

            var tagIds = request.TagIds.Distinct().ToArray();
            var existingTagIds = await db.Tags
                .Where(tag => tagIds.Contains(tag.Id))
                .Select(tag => tag.Id)
                .ToArrayAsync();
            if (existingTagIds.Length != tagIds.Length)
            {
                return Results.BadRequest("Un o més tags no existeixen.");
            }

            budgetItem.Type = type;
            budgetItem.Description = request.Description.Trim();
            budgetItem.ExpectedAmount = decimal.Round(request.ExpectedAmount, 2);
            var linksToRemove = budgetItem.BudgetItemTags
                .Where(link => !tagIds.Contains(link.TagId))
                .ToArray();
            db.BudgetItemTags.RemoveRange(linksToRemove);

            var currentTagIds = budgetItem.BudgetItemTags
                .Select(link => link.TagId)
                .ToHashSet();
            foreach (var tagId in tagIds.Where(tagId => !currentTagIds.Contains(tagId)))
            {
                budgetItem.BudgetItemTags.Add(new BudgetItemTag
                {
                    BudgetItemId = budgetItem.Id,
                    TagId = tagId
                });
            }
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapDelete("/{budgetId:int}/items/{itemId:int}", async (
            int budgetId,
            int itemId,
            FinanceDbContext db) =>
        {
            var item = await db.BudgetItems
                .FirstOrDefaultAsync(item => item.Id == itemId && item.BudgetId == budgetId);
            if (item is null)
            {
                return Results.NotFound();
            }

            db.BudgetItems.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapDelete("/{budgetId:int}", async (int budgetId, FinanceDbContext db) =>
        {
            var budget = await db.Budgets.FindAsync(budgetId);
            if (budget is null)
            {
                return Results.NotFound();
            }

            db.Budgets.Remove(budget);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return app;
    }

    private static BudgetDto MapBudget(
        Budget budget,
        IReadOnlyCollection<FinanceTransaction> transactions) => new(
        budget.Id,
        budget.Name,
        budget.From,
        budget.To,
        budget.Items.Where(item => item.Type == TransactionType.Income).Sum(item => item.ExpectedAmount),
        budget.Items.Where(item => item.Type == TransactionType.Expense).Sum(item => item.ExpectedAmount),
        budget.Items.Where(item => item.Type == TransactionType.Income).Sum(item => item.ExpectedAmount)
            - budget.Items.Where(item => item.Type == TransactionType.Expense).Sum(item => item.ExpectedAmount),
        budget.Items
            .OrderByDescending(item => item.ExpectedAmount)
            .ThenBy(item => item.Description)
            .Select(item => new BudgetItemDto(
                item.Id,
                item.Type.ToString().ToLowerInvariant(),
                item.Description,
                item.ExpectedAmount,
                item.BudgetItemTags
                    .OrderBy(link => link.Tag.TagGroup.Name)
                    .ThenBy(link => link.Tag.Name)
                    .Select(link => new TransactionTagDto(
                        link.Tag.Id,
                        link.Tag.TagGroupId,
                        link.Tag.TagGroup.Name,
                        link.Tag.Name,
                        link.Tag.Color))
                    .ToArray()))
            .ToArray(),
        BuildComparisons(budget, transactions));

    private static IReadOnlyCollection<BudgetComparisonDto> BuildComparisons(
        Budget budget,
        IReadOnlyCollection<FinanceTransaction> transactions)
    {
        var expenseItems = budget.Items
            .Where(item => item.Type == TransactionType.Expense)
            .Select(item => new
            {
                Item = item,
                TypeTags = item.BudgetItemTags
                    .Where(link => IsGroup(link.Tag.TagGroup.Name, "tipus"))
                    .Select(link => link.Tag)
                    .OrderBy(tag => tag.Id)
                    .ToArray(),
                SubtypeTags = item.BudgetItemTags
                    .Where(link => IsGroup(link.Tag.TagGroup.Name, "subtipus"))
                    .Select(link => link.Tag)
                    .OrderBy(tag => tag.Id)
                    .ToArray()
            })
            .Where(item => item.TypeTags.Length > 0 || item.SubtypeTags.Length > 0)
            .Select(item => new
            {
                item.Item,
                item.TypeTags,
                item.SubtypeTags,
                Key = $"{string.Join('-', item.TypeTags.Select(tag => tag.Id))}|{string.Join('-', item.SubtypeTags.Select(tag => tag.Id))}"
            })
            .ToArray();

        var periodExpenses = transactions
            .Where(transaction =>
                transaction.Type == TransactionType.Expense &&
                transaction.Date >= budget.From &&
                transaction.Date <= budget.To)
            .ToArray();

        return expenseItems
            .GroupBy(item => item.Key)
            .Select(group =>
            {
                var sample = group.First();
                var requiredTagIds = sample.TypeTags
                    .Concat(sample.SubtypeTags)
                    .Select(tag => tag.Id)
                    .ToArray();
                var actualExpense = periodExpenses
                    .Where(transaction => requiredTagIds.All(tagId =>
                        transaction.TransactionTags.Any(link => link.TagId == tagId)))
                    .Sum(transaction => transaction.Amount);
                var expectedExpense = group.Sum(item => item.Item.ExpectedAmount);
                var difference = actualExpense - expectedExpense;
                var usagePercentage = expectedExpense == 0
                    ? 0
                    : decimal.Round(actualExpense / expectedExpense * 100, 1);

                return new BudgetComparisonDto(
                    group.Key,
                    sample.TypeTags.Length == 0
                        ? "Sense tipus"
                        : string.Join(", ", sample.TypeTags.Select(tag => tag.Name)),
                    sample.SubtypeTags.Length == 0
                        ? "Sense subtipus"
                        : string.Join(", ", sample.SubtypeTags.Select(tag => tag.Name)),
                    expectedExpense,
                    actualExpense,
                    difference,
                    usagePercentage,
                    actualExpense > expectedExpense);
            })
            .OrderByDescending(item => item.IsExceeded)
            .ThenByDescending(item => item.UsagePercentage)
            .ToArray();
    }

    private static bool IsGroup(string groupName, string expectedName) =>
        string.Equals(groupName, expectedName, StringComparison.CurrentCultureIgnoreCase);
}
