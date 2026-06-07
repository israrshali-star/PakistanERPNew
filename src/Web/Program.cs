using PakistanAccountingERP.Application;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Infrastructure;
using PakistanAccountingERP.Infrastructure.Data.Seed;
using PakistanAccountingERP.Infrastructure.Services;
using PakistanAccountingERP.Web.Extensions;
using Serilog;

if (TryRunQuickBooksImport(args, out var importExitCode))
{
    Environment.Exit(importExitCode);
}

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            path: Path.Combine("App_Data", "Logs", "pa-erp-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            shared: true));

    builder.Services.AddControllersWithViews()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
            options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        });
    builder.Services.AddDistributedMemoryCache();
    builder.Services.AddSession(options =>
    {
        options.IdleTimeout = TimeSpan.FromMinutes(
            builder.Configuration.GetValue("AppSettings:SessionTimeoutMinutes", 60));
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
    });
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();
    builder.Services.AddWebServices();
    builder.Services.AddAppHealthChecks();
    builder.Services.AddHostedService<ScheduledBackupHostedService>();

    var app = builder.Build();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    app.UseSerilogRequestLogging();
    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseRouting();
    app.UseSession();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapAppHealthChecks();
    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");

    await DbInitializer.InitializeAsync(app.Services);

    Log.Information("Pakistan Accounting ERP started ({Environment})", app.Environment.EnvironmentName);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

static bool TryRunQuickBooksImport(string[] args, out int exitCode)
{
    exitCode = 0;

    var isIifImport = args.Length >= 4
        && string.Equals(args[0], "--import-iif", StringComparison.OrdinalIgnoreCase)
        && string.Equals(args[2], "--company-id", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(args[3], out _);

    var isReportImport = args.Length >= 3
        && string.Equals(args[0], "--import-qb-reports", StringComparison.OrdinalIgnoreCase)
        && string.Equals(args[1], "--company-id", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(args[2], out _);

    if (!isIifImport && !isReportImport)
    {
        return false;
    }

    var companyId = isIifImport ? int.Parse(args[3]) : int.Parse(args[2]);
    var options = ParseQuickBooksImportOptions(args, isIifImport ? 4 : 3);
    var filePath = isIifImport ? Path.GetFullPath(args[1]) : string.Empty;

    var builder = WebApplication.CreateBuilder();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();

    var app = builder.Build();

    var scope = app.Services.CreateAsyncScope();
    try
    {
        var importer = scope.ServiceProvider.GetRequiredService<IQuickBooksIifImportService>();
        var result = isIifImport
            ? importer.ImportAsync(filePath, companyId, options).GetAwaiter().GetResult()
            : importer.ImportReportsAsync(companyId, options).GetAwaiter().GetResult();

        Console.WriteLine(result.Message);
        Console.WriteLine($"Accounts: {result.AccountsImported} imported, {result.AccountsSkipped} skipped");
        Console.WriteLine($"Items: {result.ItemsImported} imported, {result.ItemsSkipped} skipped");
        Console.WriteLine($"Customers: {result.CustomersImported} imported, {result.CustomersSkipped} skipped");
        Console.WriteLine($"Vendors: {result.VendorsImported} imported, {result.VendorsSkipped} skipped");
        Console.WriteLine($"Invoices: {result.InvoicesImported} imported, {result.InvoicesSkipped} skipped");
        Console.WriteLine($"Bills: {result.BillsImported} imported, {result.BillsSkipped} skipped");
        Console.WriteLine($"Customer balances updated: {result.CustomerBalancesUpdated}");
        Console.WriteLine($"Vendor balances updated: {result.VendorBalancesUpdated}");

        if (isIifImport && result.Success && result.InvoicesImported == 0 && result.BillsImported == 0
            && result.CustomerBalancesUpdated == 0 && result.VendorBalancesUpdated == 0)
        {
            Console.WriteLine();
            Console.WriteLine("Normal.IIF only contains master lists (accounts, items, customers, vendors).");
            Console.WriteLine("Export these QuickBooks reports to CSV, then re-run with:");
            Console.WriteLine("  --customer-balances-csv \"path\\Customer Balance Summary.csv\"");
            Console.WriteLine("  --vendor-balances-csv \"path\\Vendor Balance Summary.csv\"");
            Console.WriteLine("  --open-invoices-csv \"path\\Open Invoices.csv\"");
            Console.WriteLine("  --open-bills-csv \"path\\Unpaid Bills.csv\"");
        }

        exitCode = result.Success ? 0 : 1;
    }
    finally
    {
        scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    return true;
}

static QuickBooksIifImportOptions ParseQuickBooksImportOptions(string[] args, int startIndex)
{
    var options = new QuickBooksIifImportOptions();

    for (var i = startIndex; i < args.Length; i++)
    {
        switch (args[i].ToLowerInvariant())
        {
            case "--customer-balances-csv" when i + 1 < args.Length:
                options.CustomerBalancesCsvPath = Path.GetFullPath(args[++i]);
                break;
            case "--vendor-balances-csv" when i + 1 < args.Length:
                options.VendorBalancesCsvPath = Path.GetFullPath(args[++i]);
                break;
            case "--open-invoices-csv" when i + 1 < args.Length:
                options.OpenInvoicesCsvPath = Path.GetFullPath(args[++i]);
                break;
            case "--open-bills-csv" when i + 1 < args.Length:
                options.OpenBillsCsvPath = Path.GetFullPath(args[++i]);
                break;
            case "--skip-master-data":
                options.SkipMasterData = true;
                break;
        }
    }

    return options;
}
