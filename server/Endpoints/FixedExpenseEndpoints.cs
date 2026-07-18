using Microsoft.EntityFrameworkCore;
using PersonalFinances.Api.Contracts;
using PersonalFinances.Api.Data;
using PersonalFinances.Api.Models;

namespace PersonalFinances.Api.Endpoints;

public static class FixedExpenseEndpoints
{
    public static IEndpointRouteBuilder MapFixedExpenseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/fixed-expenses");

        group.MapGet("/", async (FinanceDbContext db) =>
        {
            var fixedExpenses = await db.FixedExpenses
                .AsNoTracking()
                .Include(item => item.FixedExpenseTags)
                .ThenInclude(item => item.Tag)
                .ThenInclude(item => item.TagGroup)
                .OrderBy(item => item.Month)
                .ThenBy(item => item.Description)
                .ToListAsync();

            return fixedExpenses.Select(MapFixedExpense);
        });

        group.MapPost("/", async (
            CreateFixedExpenseRequest request,
            FinanceDbContext db) =>
        {
            var validation = await ValidateRequestAsync(request, db);
            if (validation is not null)
            {
                return Results.BadRequest(validation);
            }

            Enum.TryParse<TransactionType>(request.Type, true, out var type);
            var fixedExpense = new FixedExpense
            {
                Type = type,
                Description = request.Description.Trim(),
                Amount = decimal.Round(request.Amount, 2),
                Month = request.Month,
                FixedExpenseTags = request.TagIds
                    .Distinct()
                    .Select(tagId => new FixedExpenseTag { TagId = tagId })
                    .ToArray()
            };
            db.FixedExpenses.Add(fixedExpense);
            await db.SaveChangesAsync();
            return Results.Created($"/api/fixed-expenses/{fixedExpense.Id}", new { id = fixedExpense.Id });
        });

        group.MapPut("/{id:int}", async (
            int id,
            CreateFixedExpenseRequest request,
            FinanceDbContext db) =>
        {
            var fixedExpense = await db.FixedExpenses
                .Include(item => item.FixedExpenseTags)
                .FirstOrDefaultAsync(item => item.Id == id);
            if (fixedExpense is null)
            {
                return Results.NotFound("La despesa fixa no existeix.");
            }

            var validation = await ValidateRequestAsync(request, db);
            if (validation is not null)
            {
                return Results.BadRequest(validation);
            }

            Enum.TryParse<TransactionType>(request.Type, true, out var type);
            fixedExpense.Type = type;
            fixedExpense.Description = request.Description.Trim();
            fixedExpense.Amount = decimal.Round(request.Amount, 2);
            fixedExpense.Month = request.Month;

            var tagIds = request.TagIds.Distinct().ToArray();
            var linksToRemove = fixedExpense.FixedExpenseTags
                .Where(link => !tagIds.Contains(link.TagId))
                .ToArray();
            db.FixedExpenseTags.RemoveRange(linksToRemove);

            var currentTagIds = fixedExpense.FixedExpenseTags
                .Select(link => link.TagId)
                .ToHashSet();
            foreach (var tagId in tagIds.Where(tagId => !currentTagIds.Contains(tagId)))
            {
                fixedExpense.FixedExpenseTags.Add(new FixedExpenseTag
                {
                    FixedExpenseId = fixedExpense.Id,
                    TagId = tagId
                });
            }

            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapDelete("/{id:int}", async (int id, FinanceDbContext db) =>
        {
            var fixedExpense = await db.FixedExpenses.FindAsync(id);
            if (fixedExpense is null)
            {
                return Results.NotFound();
            }

            db.FixedExpenses.Remove(fixedExpense);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return app;
    }

    private static async Task<string?> ValidateRequestAsync(CreateFixedExpenseRequest request, FinanceDbContext db)
    {
        if (string.IsNullOrWhiteSpace(request.Description) || request.Amount <= 0)
        {
            return "Indica un concepte i un import superior a zero.";
        }

        if (request.Month is < 1 or > 12)
        {
            return "El mes ha d'estar entre 1 i 12.";
        }

        if (!Enum.TryParse<TransactionType>(request.Type, true, out _))
        {
            return "El tipus ha de ser 'income' o 'expense'.";
        }

        var tagIds = request.TagIds.Distinct().ToArray();
        var existingTagIds = await db.Tags
            .Where(tag => tagIds.Contains(tag.Id))
            .Select(tag => tag.Id)
            .ToArrayAsync();
        if (existingTagIds.Length != tagIds.Length)
        {
            return "Un o més tags no existeixen.";
        }

        return null;
    }

    private static FixedExpenseDto MapFixedExpense(FixedExpense item) =>
        new(
            item.Id,
            item.Type.ToString().ToLowerInvariant(),
            item.Description,
            item.Amount,
            item.Month,
            item.FixedExpenseTags
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
