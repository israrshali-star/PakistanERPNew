using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Infrastructure.Data;

namespace PakistanAccountingERP.Infrastructure.Services;

public class SqlFinancialReportDataSource : ISqlFinancialReportDataSource
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SqlFinancialReportDataSource> _logger;
    private static int _deployed;

    public SqlFinancialReportDataSource(
        AppDbContext db,
        IConfiguration configuration,
        ILogger<SqlFinancialReportDataSource> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task EnsureProceduresDeployedAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _deployed, 1, 0) == 1)
        {
            return;
        }

        var script = await LoadSqlScriptAsync(cancellationToken);
        if (script is null)
        {
            Interlocked.Exchange(ref _deployed, 0);
            _logger.LogWarning("FinancialReports.sql not found; SQL financial reports will not be available.");
            return;
        }

        foreach (var batch in SplitGoBatches(script))
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            await _db.Database.ExecuteSqlRawAsync(batch, cancellationToken);
        }

        _logger.LogInformation("Deployed SQL financial report procedures.");
    }

    public async Task<IReadOnlyList<TrialBalanceLineDto>> GetTrialBalanceLinesAsync(
        int companyId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        await EnsureProceduresDeployedAsync(cancellationToken);

        var lines = new List<TrialBalanceLineDto>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("dbo.usp_Rpt_TrialBalance", connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 120
        };
        command.Parameters.AddWithValue("@CompanyId", companyId);
        command.Parameters.AddWithValue("@FromDate", fromDate.Date);
        command.Parameters.AddWithValue("@ToDate", toDate.Date);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(new TrialBalanceLineDto(
                reader.GetInt32(reader.GetOrdinal("AccountId")),
                reader.GetString(reader.GetOrdinal("AccountNumber")),
                reader.GetString(reader.GetOrdinal("AccountName")),
                reader.IsDBNull(reader.GetOrdinal("TypeName")) ? null : reader.GetString(reader.GetOrdinal("TypeName")),
                reader.GetDecimal(reader.GetOrdinal("OpeningBalance")),
                reader.GetDecimal(reader.GetOrdinal("PeriodDebit")),
                reader.GetDecimal(reader.GetOrdinal("PeriodCredit")),
                reader.GetDecimal(reader.GetOrdinal("ClosingBalance")),
                reader.GetDecimal(reader.GetOrdinal("ClosingDebit")),
                reader.GetDecimal(reader.GetOrdinal("ClosingCredit"))));
        }

        return lines;
    }

    public async Task<IReadOnlyList<ProfitAndLossLineDto>> GetProfitAndLossLinesAsync(
        int companyId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        await EnsureProceduresDeployedAsync(cancellationToken);

        var lines = new List<ProfitAndLossLineDto>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("dbo.usp_Rpt_ProfitAndLoss", connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 120
        };
        command.Parameters.AddWithValue("@CompanyId", companyId);
        command.Parameters.AddWithValue("@FromDate", fromDate.Date);
        command.Parameters.AddWithValue("@ToDate", toDate.Date);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(new ProfitAndLossLineDto(
                reader.GetInt32(reader.GetOrdinal("AccountId")),
                reader.GetString(reader.GetOrdinal("AccountNumber")),
                reader.GetString(reader.GetOrdinal("AccountName")),
                reader.GetString(reader.GetOrdinal("Section")),
                reader.GetDecimal(reader.GetOrdinal("Amount"))));
        }

        return lines;
    }

    public async Task<IReadOnlyList<BalanceSheetLineDto>> GetBalanceSheetLinesAsync(
        int companyId,
        DateTime asOfDate,
        CancellationToken cancellationToken = default)
    {
        await EnsureProceduresDeployedAsync(cancellationToken);

        var lines = new List<BalanceSheetLineDto>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("dbo.usp_Rpt_BalanceSheet", connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 120
        };
        command.Parameters.AddWithValue("@CompanyId", companyId);
        command.Parameters.AddWithValue("@AsOfDate", asOfDate.Date);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(new BalanceSheetLineDto(
                reader.GetInt32(reader.GetOrdinal("AccountId")),
                reader.GetString(reader.GetOrdinal("AccountNumber")),
                reader.GetString(reader.GetOrdinal("AccountName")),
                reader.GetString(reader.GetOrdinal("Section")),
                reader.GetDecimal(reader.GetOrdinal("Amount"))));
        }

        return lines;
    }

    public async Task<IReadOnlyList<ArAgingLineDto>> GetArAgingLinesAsync(
        int companyId,
        DateTime asOfDate,
        CancellationToken cancellationToken = default)
    {
        await EnsureProceduresDeployedAsync(cancellationToken);

        var lines = new List<ArAgingLineDto>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("dbo.usp_Rpt_ArAging", connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 180
        };
        command.Parameters.AddWithValue("@CompanyId", companyId);
        command.Parameters.AddWithValue("@AsOfDate", asOfDate.Date);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(new ArAgingLineDto(
                reader.GetInt32(reader.GetOrdinal("CustomerId")),
                reader.GetString(reader.GetOrdinal("CustomerCode")),
                reader.GetString(reader.GetOrdinal("CustomerName")),
                reader.GetDecimal(reader.GetOrdinal("OpeningBalance")),
                reader.GetDecimal(reader.GetOrdinal("Current")),
                reader.GetDecimal(reader.GetOrdinal("Days31To60")),
                reader.GetDecimal(reader.GetOrdinal("Days61To90")),
                reader.GetDecimal(reader.GetOrdinal("Over90")),
                reader.GetDecimal(reader.GetOrdinal("Total"))));
        }

        return lines;
    }

    private SqlConnection CreateConnection()
    {
        var cs = _configuration.GetConnectionString("DefaultConnection")
                 ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        return new SqlConnection(cs);
    }

    private static async Task<string?> LoadSqlScriptAsync(CancellationToken cancellationToken)
    {
        const string resourceName = "PakistanAccountingERP.Infrastructure.Data.Sql.FinancialReports.sql";
        var assembly = typeof(SqlFinancialReportDataSource).Assembly;
        await using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is not null)
        {
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(cancellationToken);
        }

        var path = ResolveSqlScriptPath();
        return path is null ? null : await File.ReadAllTextAsync(path, cancellationToken);
    }

    private static string? ResolveSqlScriptPath()
    {
        var asmDir = Path.GetDirectoryName(typeof(SqlFinancialReportDataSource).Assembly.Location);
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(asmDir))
        {
            candidates.Add(Path.Combine(asmDir, "Data", "Sql", "FinancialReports.sql"));
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Data", "Sql", "FinancialReports.sql"));
        candidates.Add(Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "src", "Infrastructure", "Data", "Sql", "FinancialReports.sql")));
        candidates.Add(Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Infrastructure", "Data", "Sql", "FinancialReports.sql")));

        return candidates.FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> SplitGoBatches(string script)
    {
        using var reader = new StringReader(script);
        var batch = new System.Text.StringBuilder();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                yield return batch.ToString();
                batch.Clear();
                continue;
            }

            batch.AppendLine(line);
        }

        if (batch.Length > 0)
        {
            yield return batch.ToString();
        }
    }
}
