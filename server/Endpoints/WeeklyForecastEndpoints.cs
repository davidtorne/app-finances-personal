using Microsoft.EntityFrameworkCore;
using PersonalFinances.Api.Contracts;
using PersonalFinances.Api.Data;
using PersonalFinances.Api.Models;
using PersonalFinances.Api.Services;

namespace PersonalFinances.Api.Endpoints;

public static class WeeklyForecastEndpoints
{
    private const int HistoryMonths = 6;

    // (start day, end day of the month bucket; 0 means "until the end of the month")
    private static readonly (int Start, int End)[] WeekDayRanges =
    [
        (1, 7),
        (8, 14),
        (15, 21),
        (22, 0),
    ];

    public static IEndpointRouteBuilder MapWeeklyForecastEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/weekly-forecast", async (DateOnly? anchor, FinanceDbContext db) =>
        {
            var anchorDate = anchor ?? DateOnly.FromDateTime(DateTime.Today);
            var monthStart = new DateOnly(anchorDate.Year, anchorDate.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var today = DateOnly.FromDateTime(DateTime.Today);

            var (monthIncome, incomeSource) = await GetMonthIncomeAsync(db, monthStart, monthEnd);

            // Income for future weeks is still estimated from the historical day-of-month pattern.
            var historyStart = monthStart.AddMonths(-HistoryMonths);
            var historicalIncomeTransactions = await db.Transactions
                .AsNoTracking()
                .Include(item => item.TransactionTags)
                .ThenInclude(link => link.Tag)
                .ThenInclude(tag => tag.TagGroup)
                .Where(item => item.Type == TransactionType.Income && item.Date >= historyStart && item.Date < monthStart)
                .ToListAsync();

            // Expenses for future weeks come from the categories the user has manually assigned to a week.
            var detectedCategories = await ForecastCategoryCalculator.DetectCategoriesAsync(db, monthStart);
            var assignments = await db.ForecastCategoryAssignments
                .AsNoTracking()
                .Where(item => item.Week != null)
                .ToDictionaryAsync(item => item.CategoryKey, item => item.Week!.Value);

            var categoriesByWeek = new List<ForecastCategoryCalculator.CategoryInfo>[WeekDayRanges.Length];
            for (var i = 0; i < categoriesByWeek.Length; i++)
            {
                categoriesByWeek[i] = [];
            }

            foreach (var category in detectedCategories)
            {
                if (assignments.TryGetValue(category.Key, out var week) && week is >= 1 && week <= WeekDayRanges.Length)
                {
                    categoriesByWeek[week - 1].Add(category);
                }
            }

            var weeks = new List<WeeklyForecastWeekDto>();
            var cumulativeExpense = 0m;

            for (var i = 0; i < WeekDayRanges.Length; i++)
            {
                var (startDay, endDay) = WeekDayRanges[i];
                var weekFrom = new DateOnly(anchorDate.Year, anchorDate.Month, startDay);
                var weekTo = endDay == 0 ? monthEnd : new DateOnly(anchorDate.Year, anchorDate.Month, endDay);
                var isCurrent = today >= weekFrom && today <= weekTo;
                var isEstimate = weekFrom > today;

                decimal weekIncome;
                decimal weekExpense;
                IReadOnlyCollection<TagTotalDto> tagTotals;

                if (isEstimate)
                {
                    weekIncome = GetHistoricalIncomeAverage(historicalIncomeTransactions, monthStart, startDay, endDay);
                    weekExpense = categoriesByWeek[i].Sum(item => item.AverageMonthlyAmount);
                    tagTotals = BuildEstimatedTagTotals(historicalIncomeTransactions, monthStart, startDay, endDay, categoriesByWeek[i]);
                }
                else
                {
                    var actualTransactions = await db.Transactions
                        .AsNoTracking()
                        .Include(item => item.TransactionTags)
                        .ThenInclude(link => link.Tag)
                        .ThenInclude(tag => tag.TagGroup)
                        .Where(item => item.Date >= weekFrom && item.Date <= weekTo)
                        .ToListAsync();

                    weekIncome = actualTransactions.Where(item => item.Type == TransactionType.Income).Sum(item => item.Amount);
                    weekExpense = actualTransactions.Where(item => item.Type == TransactionType.Expense).Sum(item => item.Amount);
                    tagTotals = BuildActualTagTotals(actualTransactions);
                }

                cumulativeExpense += weekExpense;

                weeks.Add(new WeeklyForecastWeekDto(
                    i + 1,
                    weekFrom,
                    weekTo,
                    isCurrent,
                    isEstimate,
                    weekIncome,
                    weekExpense,
                    monthIncome - cumulativeExpense,
                    tagTotals));
            }

            return Results.Ok(new WeeklyForecastDto(
                monthStart,
                monthEnd,
                monthIncome,
                incomeSource,
                weeks));
        });

        return app;
    }

    private static async Task<(decimal Income, string Source)> GetMonthIncomeAsync(
        FinanceDbContext db,
        DateOnly monthStart,
        DateOnly monthEnd)
    {
        var budget = await db.Budgets
            .AsNoTracking()
            .Include(item => item.Items)
            .Where(item => item.From == monthStart && item.To == monthEnd)
            .OrderByDescending(item => item.Id)
            .FirstOrDefaultAsync();

        if (budget is not null)
        {
            var expectedIncome = budget.Items
                .Where(item => item.Type == TransactionType.Income)
                .Sum(item => item.ExpectedAmount);
            return (expectedIncome, "budget");
        }

        var actualIncome = await db.Transactions
            .Where(item => item.Type == TransactionType.Income && item.Date >= monthStart && item.Date <= monthEnd)
            .SumAsync(item => item.Amount);
        return (actualIncome, "actual");
    }

    private static (DateOnly From, DateOnly To) GetHistoricalRange(DateOnly monthStart, int monthsAgo, int startDay, int endDay)
    {
        var historicalMonth = monthStart.AddMonths(-monthsAgo);
        var from = new DateOnly(historicalMonth.Year, historicalMonth.Month, startDay);
        var to = endDay == 0
            ? historicalMonth.AddMonths(1).AddDays(-1)
            : new DateOnly(historicalMonth.Year, historicalMonth.Month, endDay);
        return (from, to);
    }

    private static decimal GetHistoricalIncomeAverage(
        IReadOnlyCollection<FinanceTransaction> historicalIncomeTransactions,
        DateOnly monthStart,
        int startDay,
        int endDay)
    {
        var totals = new List<decimal>();
        for (var monthsAgo = 1; monthsAgo <= HistoryMonths; monthsAgo++)
        {
            var (histFrom, histTo) = GetHistoricalRange(monthStart, monthsAgo, startDay, endDay);
            var sum = historicalIncomeTransactions
                .Where(item => item.Date >= histFrom && item.Date <= histTo)
                .Sum(item => item.Amount);
            totals.Add(sum);
        }

        return totals.Count > 0 ? totals.Average() : 0;
    }

    private static IReadOnlyCollection<TagTotalDto> BuildEstimatedTagTotals(
        IReadOnlyCollection<FinanceTransaction> historicalIncomeTransactions,
        DateOnly monthStart,
        int startDay,
        int endDay,
        IReadOnlyCollection<ForecastCategoryCalculator.CategoryInfo> weekCategories)
    {
        var accumulators = new Dictionary<int, TagAccumulator>();

        for (var monthsAgo = 1; monthsAgo <= HistoryMonths; monthsAgo++)
        {
            var (histFrom, histTo) = GetHistoricalRange(monthStart, monthsAgo, startDay, endDay);
            var matching = historicalIncomeTransactions.Where(item => item.Date >= histFrom && item.Date <= histTo);

            foreach (var transaction in matching)
            {
                foreach (var link in transaction.TransactionTags)
                {
                    var accumulator = GetOrCreateAccumulator(accumulators, link.Tag);
                    accumulator.Income += transaction.Amount / HistoryMonths;
                }
            }
        }

        foreach (var category in weekCategories)
        {
            foreach (var tag in category.TypeTags.Concat(category.SubtypeTags))
            {
                var accumulator = GetOrCreateAccumulator(accumulators, tag);
                accumulator.Expense += category.AverageMonthlyAmount;
            }
        }

        return ToTagTotals(accumulators);
    }

    private static IReadOnlyCollection<TagTotalDto> BuildActualTagTotals(
        IReadOnlyCollection<FinanceTransaction> transactions)
    {
        var accumulators = new Dictionary<int, TagAccumulator>();

        foreach (var transaction in transactions)
        {
            foreach (var link in transaction.TransactionTags)
            {
                var accumulator = GetOrCreateAccumulator(accumulators, link.Tag);
                if (transaction.Type == TransactionType.Income)
                {
                    accumulator.Income += transaction.Amount;
                }
                else
                {
                    accumulator.Expense += transaction.Amount;
                }
            }
        }

        return ToTagTotals(accumulators);
    }

    private static TagAccumulator GetOrCreateAccumulator(Dictionary<int, TagAccumulator> accumulators, Tag tag)
    {
        if (!accumulators.TryGetValue(tag.Id, out var accumulator))
        {
            accumulator = new TagAccumulator
            {
                TagId = tag.Id,
                TagName = tag.Name,
                GroupName = tag.TagGroup.Name,
                Color = tag.Color,
            };
            accumulators[tag.Id] = accumulator;
        }

        return accumulator;
    }

    private static IReadOnlyCollection<TagTotalDto> ToTagTotals(Dictionary<int, TagAccumulator> accumulators) =>
        accumulators.Values
            .Where(item => item.Income != 0 || item.Expense != 0)
            .Select(item => new TagTotalDto(
                item.TagId,
                item.TagName,
                item.GroupName,
                item.Color,
                item.Income,
                item.Expense,
                item.Income - item.Expense))
            .OrderByDescending(item => item.Expense)
            .ThenByDescending(item => item.Income)
            .ToArray();

    private sealed class TagAccumulator
    {
        public int TagId;
        public string TagName = "";
        public string GroupName = "";
        public string Color = "";
        public decimal Income;
        public decimal Expense;
    }
}
