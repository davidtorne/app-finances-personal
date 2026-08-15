using Microsoft.EntityFrameworkCore;
using PersonalFinances.Api.Models;

namespace PersonalFinances.Api.Data;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(FinanceDbContext db)
    {
        if (await db.TagGroups.AnyAsync())
        {
            return;
        }

        var provider = new TagGroup
        {
            Name = "Proveïdor",
            SelectionMode = TagSelectionMode.Single,
            Tags =
            [
                new Tag { Name = "Supermercat", Color = "#2563eb" },
                new Tag { Name = "Amazon", Color = "#f59e0b" },
                new Tag { Name = "Serveis", Color = "#8b5cf6" }
            ]
        };

        var type = new TagGroup
        {
            Name = "Tipus",
            SelectionMode = TagSelectionMode.Single,
            IsRequired = true,
            Tags =
            [
                new Tag { Name = "Alimentació", Color = "#16a34a" },
                new Tag { Name = "Habitatge", Color = "#dc2626" },
                new Tag { Name = "Transport", Color = "#0891b2" },
                new Tag { Name = "Ingressos", Color = "#059669" }
            ]
        };

        var subtype = new TagGroup
        {
            Name = "Subtipus",
            SelectionMode = TagSelectionMode.Multiple,
            Tags =
            [
                new Tag { Name = "Compra setmanal", Color = "#65a30d" },
                new Tag { Name = "Subministraments", Color = "#7c3aed" },
                new Tag { Name = "Combustible", Color = "#0e7490" },
                new Tag { Name = "Nòmina", Color = "#047857" }
            ]
        };

        db.TagGroups.AddRange(provider, type, subtype);
        await db.SaveChangesAsync();
    }

    public static async Task SeedSavingsAccountsAsync(FinanceDbContext db)
    {
        if (await db.SavingsAccounts.AnyAsync())
        {
            return;
        }

        db.SavingsAccounts.AddRange(
            new SavingsAccount { Name = "Estalvi", Color = "#2563eb" },
            new SavingsAccount { Name = "Inversió", Color = "#059669" });
        await db.SaveChangesAsync();
    }
}
