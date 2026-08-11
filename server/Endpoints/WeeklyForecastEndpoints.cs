using Microsoft.EntityFrameworkCore;
using PersonalFinances.Api.Contracts;
using PersonalFinances.Api.Data;
using PersonalFinances.Api.Models;

namespace PersonalFinances.Api.Endpoints;

public static class WeeklyForecastEndpoints
{
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

            // Expenses for future weeks come from the monthly fixed expenses the user manually entered and
            // assigned to a week.
            var monthlyFixedExpenses = await db.MonthlyFixedExpenses.AsNoTracking().ToListAsync();
            var monthlyFixedExpensesByWeek = new List<MonthlyFixedExpense>[WeekDayRanges.Length];
            for (var i = 0; i < monthlyFixedExpensesByWeek.Length; i++)
            {
                monthlyFixedExpensesByWeek[i] = [];
            }

            foreach (var monthlyFixedExpense in monthlyFixedExpenses)
            {
                if (monthlyFixedExpense.Week >= 1 && monthlyFixedExpense.Week <= WeekDayRanges.Length)
                {
                    monthlyFixedExpensesByWeek[monthlyFixedExpense.Week - 1].Add(monthlyFixedExpense);
                }
            }

            // Fixed expenses introduced for the viewed month, placed in the week the user manually assigned them to.
            var monthFixedExpenses = await db.FixedExpenses
                .AsNoTracking()
                .Include(item => item.FixedExpenseTags)
                .ThenInclude(link => link.Tag)
                .ThenInclude(tag => tag.TagGroup)
                .Where(item => item.Month == anchorDate.Month)
                .ToListAsync();

            var fixedExpensesByWeek = new List<FixedExpense>[WeekDayRanges.Length];
            for (var i = 0; i < fixedExpensesByWeek.Length; i++)
            {
                fixedExpensesByWeek[i] = [];
            }

            foreach (var fixedExpense in monthFixedExpenses)
            {
                if (fixedExpense.ForecastWeek is { } week && week >= 1 && week <= WeekDayRanges.Length)
                {
                    fixedExpensesByWeek[week - 1].Add(fixedExpense);
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

                // The forecast (previst) numbers are always computed from the manually assigned monthly
                // fixed expenses and fixed expenses, regardless of whether the week is past, current or
                // future, so the user can compare what was planned against what actually happened.
                var forecastIncome = fixedExpensesByWeek[i].Where(item => item.Type == TransactionType.Income).Sum(item => item.Amount);
                var forecastExpense = monthlyFixedExpensesByWeek[i].Sum(item => item.Amount)
                    + fixedExpensesByWeek[i].Where(item => item.Type == TransactionType.Expense).Sum(item => item.Amount);

                decimal actualIncome = 0;
                decimal actualExpense = 0;
                IReadOnlyCollection<TagTotalDto> tagTotals;

                if (isEstimate)
                {
                    tagTotals = BuildEstimatedTagTotals(fixedExpensesByWeek[i]);
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

                    actualIncome = actualTransactions.Where(item => item.Type == TransactionType.Income).Sum(item => item.Amount);
                    actualExpense = actualTransactions.Where(item => item.Type == TransactionType.Expense).Sum(item => item.Amount);
                    tagTotals = BuildActualTagTotals(actualTransactions);
                }

                // The running balance still blends forecast for future weeks with actuals for past/current
                // weeks, same as before.
                cumulativeExpense += isEstimate ? forecastExpense : actualExpense;

                weeks.Add(new WeeklyForecastWeekDto(
                    i + 1,
                    weekFrom,
                    weekTo,
                    isCurrent,
                    isEstimate,
                    forecastIncome,
                    forecastExpense,
                    actualIncome,
                    actualExpense,
                    monthIncome - cumulativeExpense,
                    tagTotals));
            }

            // What's left of the month's income once the full month's cost is covered (actuals for
            // past/current weeks, forecast for future ones — cumulativeExpense already totals that across
            // every week), spread evenly across the weeks that aren't fully over yet (current week included).
            var remainingWeeksCount = weeks.Count(item => item.IsCurrent || item.IsEstimate);
            var remainingWeeklyBudget = remainingWeeksCount > 0
                ? (monthIncome - cumulativeExpense) / remainingWeeksCount
                : 0;

            return Results.Ok(new WeeklyForecastDto(
                monthStart,
                monthEnd,
                monthIncome,
                incomeSource,
                remainingWeeklyBudget,
                remainingWeeksCount,
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
            .ThenInclude(item => item.BudgetItemTags)
            .Where(item => item.From == monthStart && item.To == monthEnd)
            .OrderByDescending(item => item.Id)
            .FirstOrDefaultAsync();

        var monthIncomeTransactions = await db.Transactions
            .AsNoTracking()
            .Include(item => item.TransactionTags)
            .Where(item => item.Type == TransactionType.Income && item.Date >= monthStart && item.Date <= monthEnd)
            .ToListAsync();

        if (budget is not null)
        {
            var expectedIncome = budget.Items
                .Where(item => item.Type == TransactionType.Income)
                .Sum(item => item.ExpectedAmount);

            // Income that doesn't match any tag used by the budget's income items wasn't part of the
            // plan (e.g. an unexpected refund or one-off job), so it's added on top of what was budgeted
            // instead of being silently dropped from the month's total.
            var budgetedIncomeTagIds = budget.Items
                .Where(item => item.Type == TransactionType.Income)
                .SelectMany(item => item.BudgetItemTags.Select(link => link.TagId))
                .ToHashSet();

            var unbudgetedIncome = monthIncomeTransactions
                .Where(transaction => !transaction.TransactionTags.Any(link => budgetedIncomeTagIds.Contains(link.TagId)))
                .Sum(transaction => transaction.Amount);

            return (expectedIncome + unbudgetedIncome, "budget");
        }

        var actualIncome = monthIncomeTransactions.Sum(item => item.Amount);
        return (actualIncome, "actual");
    }

    private static IReadOnlyCollection<TagTotalDto> BuildEstimatedTagTotals(
        IReadOnlyCollection<FixedExpense> weekFixedExpenses)
    {
        var accumulators = new Dictionary<int, TagAccumulator>();

        foreach (var fixedExpense in weekFixedExpenses)
        {
            foreach (var link in fixedExpense.FixedExpenseTags)
            {
                var accumulator = GetOrCreateAccumulator(accumulators, link.Tag);
                if (fixedExpense.Type == TransactionType.Income)
                {
                    accumulator.Income += fixedExpense.Amount;
                }
                else
                {
                    accumulator.Expense += fixedExpense.Amount;
                }
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
