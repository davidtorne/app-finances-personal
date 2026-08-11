namespace PersonalFinances.Api.Models;

public enum TransactionType
{
    Income,
    Expense
}

public enum TagSelectionMode
{
    Single,
    Multiple
}

public sealed class FinanceTransaction
{
    public int Id { get; set; }
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<TransactionTag> TransactionTags { get; set; } = [];
}

public sealed class TagGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public TagSelectionMode SelectionMode { get; set; }
    public bool IsRequired { get; set; }
    public ICollection<Tag> Tags { get; set; } = [];
}

public sealed class Tag
{
    public int Id { get; set; }
    public int TagGroupId { get; set; }
    public TagGroup TagGroup { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#64748b";
    public int? ParentTagId { get; set; }
    public Tag? ParentTag { get; set; }
    public ICollection<Tag> Children { get; set; } = [];
    public ICollection<TransactionTag> TransactionTags { get; set; } = [];
    public ICollection<BudgetItemTag> BudgetItemTags { get; set; } = [];
    public ICollection<FixedExpenseTag> FixedExpenseTags { get; set; } = [];
}

public sealed class TransactionTag
{
    public int TransactionId { get; set; }
    public FinanceTransaction Transaction { get; set; } = null!;
    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}

public sealed class Budget
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<BudgetItem> Items { get; set; } = [];
}

public sealed class BudgetItem
{
    public int Id { get; set; }
    public int BudgetId { get; set; }
    public Budget Budget { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public TransactionType Type { get; set; } = TransactionType.Expense;
    public decimal ExpectedAmount { get; set; }
    public ICollection<BudgetItemTag> BudgetItemTags { get; set; } = [];
}

public sealed class BudgetItemTag
{
    public int BudgetItemId { get; set; }
    public BudgetItem BudgetItem { get; set; } = null!;
    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}

public sealed class FixedExpense
{
    public int Id { get; set; }
    public TransactionType Type { get; set; } = TransactionType.Expense;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Month { get; set; }
    public int? ForecastWeek { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<FixedExpenseTag> FixedExpenseTags { get; set; } = [];
}

public sealed class FixedExpenseTag
{
    public int FixedExpenseId { get; set; }
    public FixedExpense FixedExpense { get; set; } = null!;
    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}

public sealed class DriveSettings
{
    public int Id { get; set; } = 1;
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? RefreshToken { get; set; }
    public string? ConnectedAccountEmail { get; set; }
    public string? FolderId { get; set; }
    public bool AutoUpload { get; set; } = true;
}

public sealed class MonthlyFixedExpense
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Week { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
