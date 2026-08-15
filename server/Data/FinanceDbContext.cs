using Microsoft.EntityFrameworkCore;
using PersonalFinances.Api.Models;

namespace PersonalFinances.Api.Data;

public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options)
    : DbContext(options)
{
    public DbSet<FinanceTransaction> Transactions => Set<FinanceTransaction>();
    public DbSet<TagGroup> TagGroups => Set<TagGroup>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TransactionTag> TransactionTags => Set<TransactionTag>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<BudgetItem> BudgetItems => Set<BudgetItem>();
    public DbSet<BudgetItemTag> BudgetItemTags => Set<BudgetItemTag>();
    public DbSet<FixedExpense> FixedExpenses => Set<FixedExpense>();
    public DbSet<FixedExpenseTag> FixedExpenseTags => Set<FixedExpenseTag>();
    public DbSet<DriveSettings> DriveSettings => Set<DriveSettings>();
    public DbSet<MonthlyFixedExpense> MonthlyFixedExpenses => Set<MonthlyFixedExpense>();
    public DbSet<SavingsAccount> SavingsAccounts => Set<SavingsAccount>();
    public DbSet<SavingsMovement> SavingsMovements => Set<SavingsMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FinanceTransaction>(entity =>
        {
            entity.Property(item => item.Amount).HasPrecision(18, 2);
            entity.Property(item => item.Description).HasMaxLength(240);
            entity.Property(item => item.Type).HasConversion<string>();
        });

        modelBuilder.Entity<TagGroup>(entity =>
        {
            entity.Property(item => item.Name).HasMaxLength(80);
            entity.Property(item => item.SelectionMode).HasConversion<string>();
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.Property(item => item.Name).HasMaxLength(80);
            entity.Property(item => item.Color).HasMaxLength(16);
            entity.HasOne(item => item.ParentTag)
                .WithMany(item => item.Children)
                .HasForeignKey(item => item.ParentTagId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.LinkedSavingsAccount)
                .WithMany()
                .HasForeignKey(item => item.LinkedSavingsAccountId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TransactionTag>(entity =>
        {
            entity.HasKey(item => new { item.TransactionId, item.TagId });
            entity.HasOne(item => item.Transaction)
                .WithMany(item => item.TransactionTags)
                .HasForeignKey(item => item.TransactionId);
            entity.HasOne(item => item.Tag)
                .WithMany(item => item.TransactionTags)
                .HasForeignKey(item => item.TagId);
        });

        modelBuilder.Entity<Budget>(entity =>
        {
            entity.Property(item => item.Name).HasMaxLength(120);
        });

        modelBuilder.Entity<BudgetItem>(entity =>
        {
            entity.Property(item => item.Description).HasMaxLength(240);
            entity.Property(item => item.Type).HasConversion<string>();
            entity.Property(item => item.ExpectedAmount).HasPrecision(18, 2);
            entity.HasOne(item => item.Budget)
                .WithMany(item => item.Items)
                .HasForeignKey(item => item.BudgetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BudgetItemTag>(entity =>
        {
            entity.HasKey(item => new { item.BudgetItemId, item.TagId });
            entity.HasOne(item => item.BudgetItem)
                .WithMany(item => item.BudgetItemTags)
                .HasForeignKey(item => item.BudgetItemId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Tag)
                .WithMany(item => item.BudgetItemTags)
                .HasForeignKey(item => item.TagId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FixedExpense>(entity =>
        {
            entity.Property(item => item.Description).HasMaxLength(240);
            entity.Property(item => item.Type).HasConversion<string>();
            entity.Property(item => item.Amount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<FixedExpenseTag>(entity =>
        {
            entity.HasKey(item => new { item.FixedExpenseId, item.TagId });
            entity.HasOne(item => item.FixedExpense)
                .WithMany(item => item.FixedExpenseTags)
                .HasForeignKey(item => item.FixedExpenseId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Tag)
                .WithMany(item => item.FixedExpenseTags)
                .HasForeignKey(item => item.TagId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DriveSettings>(entity =>
        {
            entity.Property(item => item.ClientId).HasMaxLength(240);
            entity.Property(item => item.ClientSecret).HasMaxLength(240);
            entity.Property(item => item.RefreshToken).HasMaxLength(1024);
            entity.Property(item => item.ConnectedAccountEmail).HasMaxLength(320);
            entity.Property(item => item.FolderId).HasMaxLength(120);
        });

        modelBuilder.Entity<MonthlyFixedExpense>(entity =>
        {
            entity.Property(item => item.Description).HasMaxLength(240);
            entity.Property(item => item.Amount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<SavingsAccount>(entity =>
        {
            entity.Property(item => item.Name).HasMaxLength(80);
            entity.Property(item => item.Color).HasMaxLength(16);
        });

        modelBuilder.Entity<SavingsMovement>(entity =>
        {
            entity.Property(item => item.Type).HasConversion<string>();
            entity.Property(item => item.Amount).HasPrecision(18, 2);
            entity.Property(item => item.Description).HasMaxLength(240);
            entity.HasOne(item => item.SavingsAccount)
                .WithMany(item => item.Movements)
                .HasForeignKey(item => item.SavingsAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
