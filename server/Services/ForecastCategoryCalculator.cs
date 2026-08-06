using Microsoft.EntityFrameworkCore;
using PersonalFinances.Api.Data;
using PersonalFinances.Api.Models;

namespace PersonalFinances.Api.Services;

/// <summary>
/// Detects the distinct Tipus+Subtipus expense categories seen over the last few months and their
/// average monthly amount, so the weekly forecast can let the user manually assign each one to a week
/// instead of guessing from transaction dates.
/// </summary>
public static class ForecastCategoryCalculator
{
    public const int HistoryMonths = 6;

    public sealed record CategoryInfo(
        string Key,
        IReadOnlyCollection<Tag> TypeTags,
        IReadOnlyCollection<Tag> SubtypeTags,
        decimal AverageMonthlyAmount);

    public static async Task<IReadOnlyCollection<CategoryInfo>> DetectCategoriesAsync(
        FinanceDbContext db,
        DateOnly viewedMonth)
    {
        var monthStart = new DateOnly(viewedMonth.Year, viewedMonth.Month, 1);
        var historyStart = monthStart.AddMonths(-HistoryMonths);

        var transactions = await db.Transactions
            .AsNoTracking()
            .Include(item => item.TransactionTags)
            .ThenInclude(link => link.Tag)
            .ThenInclude(tag => tag.TagGroup)
            .Where(item => item.Type == TransactionType.Expense && item.Date >= historyStart && item.Date < monthStart)
            .ToListAsync();

        return transactions
            .Select(transaction => new
            {
                Transaction = transaction,
                TypeTags = transaction.TransactionTags
                    .Where(link => IsGroup(link.Tag.TagGroup.Name, "tipus"))
                    .Select(link => link.Tag)
                    .OrderBy(tag => tag.Id)
                    .ToArray(),
                SubtypeTags = transaction.TransactionTags
                    .Where(link => IsGroup(link.Tag.TagGroup.Name, "subtipus"))
                    .Select(link => link.Tag)
                    .OrderBy(tag => tag.Id)
                    .ToArray(),
            })
            .Where(item => item.TypeTags.Length > 0 || item.SubtypeTags.Length > 0)
            .Select(item => new
            {
                item.Transaction,
                item.TypeTags,
                item.SubtypeTags,
                Key = BuildKey(item.TypeTags, item.SubtypeTags),
            })
            .GroupBy(item => item.Key)
            .Select(group =>
            {
                var sample = group.First();
                var monthlyTotals = new decimal[HistoryMonths];
                foreach (var item in group)
                {
                    var monthsAgo = ((monthStart.Year - item.Transaction.Date.Year) * 12)
                        + (monthStart.Month - item.Transaction.Date.Month);
                    if (monthsAgo is >= 1 and <= HistoryMonths)
                    {
                        monthlyTotals[monthsAgo - 1] += item.Transaction.Amount;
                    }
                }

                return new CategoryInfo(group.Key, sample.TypeTags, sample.SubtypeTags, monthlyTotals.Average());
            })
            .OrderByDescending(item => item.AverageMonthlyAmount)
            .ToArray();
    }

    private static string BuildKey(IReadOnlyCollection<Tag> typeTags, IReadOnlyCollection<Tag> subtypeTags) =>
        $"{string.Join('-', typeTags.Select(tag => tag.Id))}|{string.Join('-', subtypeTags.Select(tag => tag.Id))}";

    private static bool IsGroup(string groupName, string expectedName) =>
        string.Equals(groupName, expectedName, StringComparison.CurrentCultureIgnoreCase);
}
