using Microsoft.EntityFrameworkCore;
using PersonalFinances.Api.Contracts;
using PersonalFinances.Api.Data;
using PersonalFinances.Api.Models;

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

            var excludedTagIds = await db.ForecastExcludedTags
                .AsNoTracking()
                .Select(item => item.TagId)
                .ToHashSetAsync();

            var (monthIncome, incomeSource) = await GetMonthIncomeAsync(db, monthStart, monthEnd);

            var currentMonthFixedExpenses = await db.FixedExpenses
                .AsNoTracking()
                .Include(item => item.FixedExpenseTags)
                .ThenInclude(link => link.Tag)
                .ThenInclude(tag => tag.TagGroup)
                .Where(item => item.Month == anchorDate.Month)
                .ToListAsync();
            currentMonthFixedExpenses = currentMonthFixedExpenses
                .Where(item => !item.FixedExpenseTags.Any(link => excludedTagIds.Contains(link.TagId)))
                .ToList();

            var (fixedExpensesByWeek, fixedIncomeByWeek, fixedTagContributionsByWeek) =
                await AssignFixedExpensesToWeeksAsync(db, currentMonthFixedExpenses, anchorDate.Year, anchorDate.Month);
            var fixedExpensesTotal = currentMonthFixedExpenses
                .Where(item => item.Type == TransactionType.Expense)
                .Sum(item => item.Amount);

            var historyStart = monthStart.AddMonths(-HistoryMonths);
            var historicalTransactions = await db.Transactions
                .AsNoTracking()
                .Include(item => item.TransactionTags)
                .ThenInclude(link => link.Tag)
                .ThenInclude(tag => tag.TagGroup)
                .Where(item => item.Date >= historyStart && item.Date < monthStart)
                .ToListAsync();
            historicalTransactions = historicalTransactions
                .Where(item => !item.TransactionTags.Any(link => excludedTagIds.Contains(link.TagId)))
                .ToList();

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
                    weekIncome = GetHistoricalAverage(historicalTransactions, TransactionType.Income, monthStart, startDay, endDay)
                        + fixedIncomeByWeek[i];
                    weekExpense = GetHistoricalAverage(historicalTransactions, TransactionType.Expense, monthStart, startDay, endDay)
                        + fixedExpensesByWeek[i];
                    tagTotals = BuildEstimatedTagTotals(historicalTransactions, fixedTagContributionsByWeek[i], monthStart, startDay, endDay);
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
                fixedExpensesTotal,
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

    /// <summary>
    /// For each current-month fixed expense, finds the most recent real transaction with a matching
    /// description (searching the whole history, any year) and uses the day it happened to decide which
    /// week bucket to place the (non-fractioned) amount in. Falls back to week 1 if there's no match.
    /// Amounts are only assigned to weeks that are still in the future, to avoid double-counting a fixed
    /// expense that already shows up in this month's real transactions.
    /// </summary>
    private static async Task<(decimal[] ExpenseByWeek, decimal[] IncomeByWeek, List<(Tag Tag, decimal Amount, bool IsIncome)>[] TagContributionsByWeek)> AssignFixedExpensesToWeeksAsync(
        FinanceDbContext db,
        IReadOnlyCollection<FixedExpense> fixedExpenses,
        int year,
        int month)
    {
        var expenseByWeek = new decimal[WeekDayRanges.Length];
        var incomeByWeek = new decimal[WeekDayRanges.Length];
        var tagContributionsByWeek = new List<(Tag, decimal, bool)>[WeekDayRanges.Length];
        for (var i = 0; i < tagContributionsByWeek.Length; i++)
        {
            tagContributionsByWeek[i] = [];
        }

        var today = DateOnly.FromDateTime(DateTime.Today);

        // Loaded once and matched in memory: real transaction descriptions are often shorter/looser than
        // the fixed expense's own label (e.g. fixed expense "Domini davidtorne.com" vs a real movement
        // just called "domini"), so a strict "transaction contains the full label" SQL match misses these.
        var matchCandidates = await db.Transactions
            .AsNoTracking()
            .Select(item => new { item.Type, item.Date, item.Description })
            .ToListAsync();

        foreach (var fixedExpense in fixedExpenses)
        {
            var normalizedFixedDescription = fixedExpense.Description.Trim().ToLowerInvariant();
            var lastMatch = matchCandidates
                .Where(item => item.Type == fixedExpense.Type)
                .Where(item =>
                {
                    var normalizedTransactionDescription = item.Description.Trim().ToLowerInvariant();
                    if (normalizedTransactionDescription.Length == 0)
                    {
                        return false;
                    }

                    return normalizedTransactionDescription.Contains(normalizedFixedDescription)
                        || (normalizedTransactionDescription.Length >= 3
                            && normalizedFixedDescription.Contains(normalizedTransactionDescription));
                })
                .OrderByDescending(item => item.Date)
                .FirstOrDefault();

            var day = lastMatch?.Date.Day ?? 1;
            var weekIndex = GetWeekIndexForDay(day);
            var weekFrom = new DateOnly(year, month, WeekDayRanges[weekIndex].Start);

            if (weekFrom <= today)
            {
                continue;
            }

            if (fixedExpense.Type == TransactionType.Income)
            {
                incomeByWeek[weekIndex] += fixedExpense.Amount;
            }
            else
            {
                expenseByWeek[weekIndex] += fixedExpense.Amount;
            }

            foreach (var link in fixedExpense.FixedExpenseTags)
            {
                tagContributionsByWeek[weekIndex].Add((link.Tag, fixedExpense.Amount, fixedExpense.Type == TransactionType.Income));
            }
        }

        return (expenseByWeek, incomeByWeek, tagContributionsByWeek);
    }

    private static int GetWeekIndexForDay(int day)
    {
        for (var i = 0; i < WeekDayRanges.Length; i++)
        {
            var (start, end) = WeekDayRanges[i];
            if (day >= start && (end == 0 || day <= end))
            {
                return i;
            }
        }

        return WeekDayRanges.Length - 1;
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

    private static decimal GetHistoricalAverage(
        IReadOnlyCollection<FinanceTransaction> historicalTransactions,
        TransactionType type,
        DateOnly monthStart,
        int startDay,
        int endDay)
    {
        var totals = new List<decimal>();
        for (var monthsAgo = 1; monthsAgo <= HistoryMonths; monthsAgo++)
        {
            var (histFrom, histTo) = GetHistoricalRange(monthStart, monthsAgo, startDay, endDay);
            var sum = historicalTransactions
                .Where(item => item.Type == type && item.Date >= histFrom && item.Date <= histTo)
                .Sum(item => item.Amount);
            totals.Add(sum);
        }

        return totals.Count > 0 ? totals.Average() : 0;
    }

    private static IReadOnlyCollection<TagTotalDto> BuildEstimatedTagTotals(
        IReadOnlyCollection<FinanceTransaction> historicalTransactions,
        IReadOnlyCollection<(Tag Tag, decimal Amount, bool IsIncome)> fixedTagContributions,
        DateOnly monthStart,
        int startDay,
        int endDay)
    {
        var accumulators = new Dictionary<int, TagAccumulator>();

        for (var monthsAgo = 1; monthsAgo <= HistoryMonths; monthsAgo++)
        {
            var (histFrom, histTo) = GetHistoricalRange(monthStart, monthsAgo, startDay, endDay);
            var matching = historicalTransactions.Where(item => item.Date >= histFrom && item.Date <= histTo);

            foreach (var transaction in matching)
            {
                foreach (var link in transaction.TransactionTags)
                {
                    var accumulator = GetOrCreateAccumulator(accumulators, link.Tag);
                    if (transaction.Type == TransactionType.Income)
                    {
                        accumulator.Income += transaction.Amount / HistoryMonths;
                    }
                    else
                    {
                        accumulator.Expense += transaction.Amount / HistoryMonths;
                    }
                }
            }
        }

        foreach (var (tag, amount, isIncome) in fixedTagContributions)
        {
            var accumulator = GetOrCreateAccumulator(accumulators, tag);
            if (isIncome)
            {
                accumulator.Income += amount;
            }
            else
            {
                accumulator.Expense += amount;
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
