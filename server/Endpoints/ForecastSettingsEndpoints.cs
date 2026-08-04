using Microsoft.EntityFrameworkCore;
using PersonalFinances.Api.Contracts;
using PersonalFinances.Api.Data;
using PersonalFinances.Api.Models;

namespace PersonalFinances.Api.Endpoints;

public static class ForecastSettingsEndpoints
{
    public static IEndpointRouteBuilder MapForecastSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/forecast-settings");

        group.MapGet("/", async (FinanceDbContext db) =>
        {
            var excludedTagIds = await db.ForecastExcludedTags
                .AsNoTracking()
                .Select(item => item.TagId)
                .ToArrayAsync();

            return Results.Ok(new ForecastSettingsDto(excludedTagIds));
        });

        group.MapPost("/", async (SaveForecastSettingsRequest request, FinanceDbContext db) =>
        {
            var tagIds = request.ExcludedTagIds.Distinct().ToArray();
            var existingTagIds = await db.Tags
                .Where(tag => tagIds.Contains(tag.Id))
                .Select(tag => tag.Id)
                .ToArrayAsync();
            if (existingTagIds.Length != tagIds.Length)
            {
                return Results.BadRequest("Un o més tags no existeixen.");
            }

            var current = await db.ForecastExcludedTags.ToListAsync();
            db.ForecastExcludedTags.RemoveRange(current);
            db.ForecastExcludedTags.AddRange(tagIds.Select(tagId => new ForecastExcludedTag { TagId = tagId }));
            await db.SaveChangesAsync();

            return Results.NoContent();
        });

        return app;
    }
}
