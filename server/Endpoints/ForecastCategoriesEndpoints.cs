using Microsoft.EntityFrameworkCore;
using PersonalFinances.Api.Contracts;
using PersonalFinances.Api.Data;
using PersonalFinances.Api.Services;

namespace PersonalFinances.Api.Endpoints;

public static class ForecastCategoriesEndpoints
{
    public static IEndpointRouteBuilder MapForecastCategoriesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/forecast-categories");

        group.MapGet("/", async (FinanceDbContext db) =>
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var categories = await ForecastCategoryCalculator.DetectCategoriesAsync(
                db,
                new DateOnly(today.Year, today.Month, 1));

            var assignments = await db.ForecastCategoryAssignments
                .AsNoTracking()
                .ToDictionaryAsync(item => item.CategoryKey);

            var result = categories
                .Select(category =>
                {
                    var hasAssignment = assignments.TryGetValue(category.Key, out var assignment);
                    return new ForecastCategoryDto(
                        category.Key,
                        category.TypeTags.Count > 0
                            ? string.Join(", ", category.TypeTags.Select(tag => tag.Name))
                            : "Sense tipus",
                        category.SubtypeTags.Count > 0
                            ? string.Join(", ", category.SubtypeTags.Select(tag => tag.Name))
                            : "Sense subtipus",
                        category.AverageMonthlyAmount,
                        hasAssignment,
                        hasAssignment ? assignment!.Week : null);
                })
                .OrderByDescending(item => item.AverageMonthlyAmount)
                .ToArray();

            return Results.Ok(result);
        });

        group.MapPost("/", async (SaveForecastCategoryRequest request, FinanceDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Key))
            {
                return Results.BadRequest("Falta la clau de la categoria.");
            }

            if (request.Week is < 1 or > 4)
            {
                return Results.BadRequest("La setmana ha d'estar entre 1 i 4.");
            }

            var existing = await db.ForecastCategoryAssignments
                .FirstOrDefaultAsync(item => item.CategoryKey == request.Key);
            if (existing is null)
            {
                db.ForecastCategoryAssignments.Add(new()
                {
                    CategoryKey = request.Key,
                    Week = request.Week,
                });
            }
            else
            {
                existing.Week = request.Week;
            }

            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return app;
    }
}
