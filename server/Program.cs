using Microsoft.EntityFrameworkCore;
using PersonalFinances.Api.Data;
using PersonalFinances.Api.Endpoints;
using PersonalFinances.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "data");
Directory.CreateDirectory(dataDirectory);

builder.Services.AddDbContext<FinanceDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDirectory, "finances.db")}"));
builder.Services.AddHttpClient();
builder.Services.AddScoped<GoogleDriveService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
    await db.Database.EnsureCreatedAsync();
    await DatabaseSchema.EnsureBudgetTablesAsync(db);
    await DatabaseSchema.EnsureFixedExpenseTablesAsync(db);
    await DatabaseSchema.EnsureDriveSettingsTableAsync(db);
    await DatabaseSchema.EnsureMonthlyFixedExpenseTablesAsync(db);
    await DatabaseSchema.EnsureSavingsTablesAsync(db);
    await DatabaseSchema.EnsureTagSavingsLinkColumnAsync(db);
    await DatabaseSeeder.SeedAsync(db);
    await DatabaseSeeder.SeedSavingsAccountsAsync(db);
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapTagEndpoints();
app.MapTransactionEndpoints();
app.MapSummaryEndpoints();
app.MapWeeklyForecastEndpoints();
app.MapMonthlyFixedExpenseEndpoints();
app.MapBudgetEndpoints();
app.MapFixedExpenseEndpoints();
app.MapSavingsEndpoints();
app.MapBackupEndpoints();
app.MapDriveEndpoints();

if (Directory.Exists(app.Environment.WebRootPath))
{
    app.MapFallbackToFile("index.html");
}

app.Run();
