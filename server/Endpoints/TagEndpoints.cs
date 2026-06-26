using Microsoft.EntityFrameworkCore;
using PersonalFinances.Api.Contracts;
using PersonalFinances.Api.Data;
using PersonalFinances.Api.Models;

namespace PersonalFinances.Api.Endpoints;

public static class TagEndpoints
{
    public static IEndpointRouteBuilder MapTagEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        group.MapGet("/tag-groups", async (FinanceDbContext db) =>
        {
            var groups = await db.TagGroups
                .AsNoTracking()
                .Include(item => item.Tags)
                .OrderBy(item => item.Name)
                .ToListAsync();

            return groups.Select(item => new TagGroupDto(
                item.Id,
                item.Name,
                item.SelectionMode.ToString().ToLowerInvariant(),
                item.IsRequired,
                item.Tags
                    .OrderBy(tag => tag.Name)
                    .Select(tag => new TagDto(tag.Id, tag.Name, tag.Color, tag.ParentTagId))
                    .ToArray()));
        });

        group.MapPost("/tag-groups", async (
            CreateTagGroupRequest request,
            FinanceDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("El nom del grup és obligatori.");
            }

            if (!Enum.TryParse<TagSelectionMode>(request.SelectionMode, true, out var mode))
            {
                return Results.BadRequest("El mode ha de ser 'single' o 'multiple'.");
            }

            var tagGroup = new TagGroup
            {
                Name = request.Name.Trim(),
                SelectionMode = mode,
                IsRequired = request.IsRequired
            };

            db.TagGroups.Add(tagGroup);
            await db.SaveChangesAsync();
            return Results.Created($"/api/tag-groups/{tagGroup.Id}", tagGroup.Id);
        });

        group.MapPost("/tags", async (CreateTagRequest request, FinanceDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("El nom del tag és obligatori.");
            }

            if (!await db.TagGroups.AnyAsync(item => item.Id == request.TagGroupId))
            {
                return Results.BadRequest("El grup de tags no existeix.");
            }

            if (request.ParentTagId is int parentTagId &&
                !await db.Tags.AnyAsync(item =>
                    item.Id == parentTagId && item.TagGroupId == request.TagGroupId))
            {
                return Results.BadRequest("El tag pare ha de pertànyer al mateix grup.");
            }

            var tag = new Tag
            {
                TagGroupId = request.TagGroupId,
                Name = request.Name.Trim(),
                Color = string.IsNullOrWhiteSpace(request.Color) ? "#64748b" : request.Color,
                ParentTagId = request.ParentTagId
            };

            db.Tags.Add(tag);
            await db.SaveChangesAsync();
            return Results.Created($"/api/tags/{tag.Id}", tag.Id);
        });

        group.MapDelete("/tags/{id:int}", async (int id, FinanceDbContext db) =>
        {
            var tag = await db.Tags.FindAsync(id);
            if (tag is null)
            {
                return Results.NotFound();
            }

            if (await db.TransactionTags.AnyAsync(item => item.TagId == id))
            {
                return Results.Conflict("No es pot eliminar un tag que te moviments assignats.");
            }

            if (await db.BudgetItemTags.AnyAsync(item => item.TagId == id))
            {
                return Results.Conflict("No es pot eliminar un tag que té conceptes de pressupost assignats.");
            }

            db.Tags.Remove(tag);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return app;
    }
}
