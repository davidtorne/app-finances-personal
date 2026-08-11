using Microsoft.EntityFrameworkCore;
using PersonalFinances.Api.Contracts;
using PersonalFinances.Api.Data;
using PersonalFinances.Api.Models;

namespace PersonalFinances.Api.Endpoints;

public static class MonthlyFixedExpenseEndpoints
{
    public static IEndpointRouteBuilder MapMonthlyFixedExpenseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/monthly-fixed-expenses");

        group.MapGet("/", async (FinanceDbContext db) =>
        {
            var items = await db.MonthlyFixedExpenses
                .AsNoTracking()
                .OrderBy(item => item.Week)
                .ThenBy(item => item.Description)
                .ToListAsync();

            return items.Select(MapMonthlyFixedExpense);
        });

        group.MapPost("/", async (CreateMonthlyFixedExpenseRequest request, FinanceDbContext db) =>
        {
            var validation = Validate(request);
            if (validation is not null)
            {
                return Results.BadRequest(validation);
            }

            var item = new MonthlyFixedExpense
            {
                Description = request.Description.Trim(),
                Amount = decimal.Round(request.Amount, 2),
                Week = request.Week,
            };
            db.MonthlyFixedExpenses.Add(item);
            await db.SaveChangesAsync();
            return Results.Created($"/api/monthly-fixed-expenses/{item.Id}", new { id = item.Id });
        });

        group.MapPut("/{id:int}", async (int id, CreateMonthlyFixedExpenseRequest request, FinanceDbContext db) =>
        {
            var item = await db.MonthlyFixedExpenses.FindAsync(id);
            if (item is null)
            {
                return Results.NotFound("La despesa fixa mensual no existeix.");
            }

            var validation = Validate(request);
            if (validation is not null)
            {
                return Results.BadRequest(validation);
            }

            item.Description = request.Description.Trim();
            item.Amount = decimal.Round(request.Amount, 2);
            item.Week = request.Week;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapDelete("/{id:int}", async (int id, FinanceDbContext db) =>
        {
            var item = await db.MonthlyFixedExpenses.FindAsync(id);
            if (item is null)
            {
                return Results.NotFound();
            }

            db.MonthlyFixedExpenses.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return app;
    }

    private static string? Validate(CreateMonthlyFixedExpenseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Description) || request.Amount <= 0)
        {
            return "Indica un concepte i un import superior a zero.";
        }

        if (request.Week is < 1 or > 4)
        {
            return "La setmana ha d'estar entre 1 i 4.";
        }

        return null;
    }

    private static MonthlyFixedExpenseDto MapMonthlyFixedExpense(MonthlyFixedExpense item) =>
        new(item.Id, item.Description, item.Amount, item.Week);
}
