namespace PersonalFinances.Api.Contracts;

public sealed record TagDto(
    int Id,
    string Name,
    string Color,
    int? ParentTagId);

public sealed record TagGroupDto(
    int Id,
    string Name,
    string SelectionMode,
    bool IsRequired,
    IReadOnlyCollection<TagDto> Tags);

public sealed record TransactionDto(
    int Id,
    string Type,
    decimal Amount,
    DateOnly Date,
    string Description,
    IReadOnlyCollection<TransactionTagDto> Tags);

public sealed record TransactionTagDto(
    int Id,
    int GroupId,
    string GroupName,
    string Name,
    string Color);

public sealed record CreateTransactionRequest(
    string Type,
    decimal Amount,
    DateOnly Date,
    string Description,
    IReadOnlyCollection<int> TagIds);

public sealed record CreateTagGroupRequest(
    string Name,
    string SelectionMode,
    bool IsRequired);

public sealed record CreateTagRequest(
    int TagGroupId,
    string Name,
    string Color,
    int? ParentTagId);

public sealed record SummaryDto(
    DateOnly From,
    DateOnly To,
    decimal Income,
    decimal Expense,
    decimal Balance,
    IReadOnlyCollection<TagTotalDto> TagTotals);

public sealed record TagTotalDto(
    int TagId,
    string TagName,
    string GroupName,
    string Color,
    decimal Income,
    decimal Expense,
    decimal Balance);

public sealed record BudgetDto(
    int Id,
    string Name,
    DateOnly From,
    DateOnly To,
    decimal ExpectedIncome,
    decimal ExpectedExpense,
    decimal ExpectedBalance,
    IReadOnlyCollection<BudgetItemDto> Items,
    IReadOnlyCollection<BudgetComparisonDto> Comparisons);

public sealed record BudgetComparisonDto(
    string Key,
    string TypeName,
    string SubtypeName,
    decimal ExpectedExpense,
    decimal ActualExpense,
    decimal Difference,
    decimal UsagePercentage,
    bool IsExceeded);

public sealed record BudgetItemDto(
    int Id,
    string Type,
    string Description,
    decimal ExpectedAmount,
    IReadOnlyCollection<TransactionTagDto> Tags);

public sealed record CreateBudgetRequest(string Name, DateOnly From, DateOnly To);

public sealed record CreateBudgetItemRequest(
    string Type,
    string Description,
    decimal ExpectedAmount,
    IReadOnlyCollection<int> TagIds);
