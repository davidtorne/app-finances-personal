using Microsoft.EntityFrameworkCore;
using PersonalFinances.Api.Contracts;
using PersonalFinances.Api.Data;
using PersonalFinances.Api.Models;

namespace PersonalFinances.Api.Endpoints;

public static class SummaryEndpoints
{
    public static IEndpointRouteBuilder MapSummaryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/summary", async (
            string? period,
            DateOnly? anchor,
            int[]? tagIds,
            FinanceDbContext db) =>
        {
            var selectedPeriod = period?.ToLowerInvariant() ?? "month";
            var anchorDate = anchor ?? DateOnly.FromDateTime(DateTime.Today);
            var range = GetRange(selectedPeriod, anchorDate);

            if (range is null)
            {
                return Results.BadRequest("El període ha de ser 'week', 'month' o 'year'.");
            }

            var (from, to) = range.Value;
            var query = db.Transactions
                .AsNoTracking()
                .Include(item => item.TransactionTags)
                .ThenInclude(item => item.Tag)
                .ThenInclude(item => item.TagGroup)
                .Where(item => item.Date >= from && item.Date <= to)
                .AsQueryable();

            if (tagIds is { Length: > 0 })
            {
                foreach (var tagId in tagIds.Distinct())
                {
                    query = query.Where(item =>
                        item.TransactionTags.Any(link => link.TagId == tagId));
                }
            }

            var transactions = await query.ToListAsync();

            var income = transactions
                .Where(item => item.Type == TransactionType.Income)
                .Sum(item => item.Amount);
            var expense = transactions
                .Where(item => item.Type == TransactionType.Expense)
                .Sum(item => item.Amount);

            var tagTotals = transactions
                .SelectMany(transaction => transaction.TransactionTags.Select(link => new
                {
                    Transaction = transaction,
                    Tag = link.Tag
                }))
                .GroupBy(item => new
                {
                    item.Tag.Id,
                    item.Tag.Name,
                    item.Tag.Color,
                    GroupName = item.Tag.TagGroup.Name
                })
                .Select(items =>
                {
                    var tagIncome = items
                        .Where(item => item.Transaction.Type == TransactionType.Income)
                        .Sum(item => item.Transaction.Amount);
                    var tagExpense = items
                        .Where(item => item.Transaction.Type == TransactionType.Expense)
                        .Sum(item => item.Transaction.Amount);

                    return new TagTotalDto(
                        items.Key.Id,
                        items.Key.Name,
                        items.Key.GroupName,
                        items.Key.Color,
                        tagIncome,
                        tagExpense,
                        tagIncome - tagExpense);
                })
                .OrderByDescending(item => item.Expense)
                .ThenByDescending(item => item.Income)
                .ToArray();

            return Results.Ok(new SummaryDto(
                from,
                to,
                income,
                expense,
                income - expense,
                tagTotals));
        });

        return app;
    }

    private static (DateOnly From, DateOnly To)? GetRange(string period, DateOnly anchor)
    {
        if (period == "week")
        {
            var daysFromMonday = ((int)anchor.DayOfWeek + 6) % 7;
            var from = anchor.AddDays(-daysFromMonday);
            return (from, from.AddDays(6));
        }

        if (period == "month")
        {
            var from = new DateOnly(anchor.Year, anchor.Month, 1);
            return (from, from.AddMonths(1).AddDays(-1));
        }

        if (period == "year")
        {
            return (
                new DateOnly(anchor.Year, 1, 1),
                new DateOnly(anchor.Year, 12, 31));
        }

        return null;
    }
}
