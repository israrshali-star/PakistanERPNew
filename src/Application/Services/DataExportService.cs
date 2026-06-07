using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Application.Options;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class DataExportService : IDataExportService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly IChartOfAccountsService _chartOfAccountsService;
    private readonly ExportOptions _options;
    private readonly ILogger<DataExportService> _logger;

    public DataExportService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IChartOfAccountsService chartOfAccountsService,
        IOptions<ExportOptions> options,
        ILogger<DataExportService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _chartOfAccountsService = chartOfAccountsService;
        _options = options.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<object>> GetExportTypesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<object> result = Enum.GetValues<DataExportType>()
            .Select(x => new
            {
                id = (int)x,
                code = x.ToString(),
                name = ToLabel(x)
            })
            .ToList();
        return Task.FromResult(result);
    }

    public async Task<DataTableResponse<DataExportHistoryListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var query = _unitOfWork.Repository<DataExportHistory>()
            .Query()
            .Where(x => x.CompanyId == companyId);

        var recordsTotal = await query.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var term = request.SearchValue.Trim().ToLower();
            query = query.Where(x =>
                x.FileName.ToLower().Contains(term)
                || x.ExportType.ToString().ToLower().Contains(term)
                || (x.ErrorMessage != null && x.ErrorMessage.ToLower().Contains(term))
                || (x.CreatedBy != null && x.CreatedBy.ToLower().Contains(term)));
        }

        var recordsFiltered = await query.CountAsync(cancellationToken);
        query = ApplyOrdering(query, request);
        if (request.Length > 0)
        {
            query = query.Skip(request.Start).Take(request.Length);
        }

        var rows = await query
            .Select(x => new DataExportHistoryListItemDto(
                x.Id,
                x.ExportType,
                x.FileName,
                x.FileSizeBytes,
                x.Status,
                x.StartedAt,
                x.CompletedAt,
                x.ErrorMessage,
                x.CreatedBy))
            .ToListAsync(cancellationToken);

        return new DataTableResponse<DataExportHistoryListItemDto>(request.Draw, recordsTotal, recordsFiltered, rows);
    }

    public async Task<JobActionResult> RunExportAsync(
        DataExportType exportType,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var userName = _currentUser.UserName ?? "system";
        var startedAt = DateTime.UtcNow;
        var fileName = $"{exportType}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var companyPath = Path.Combine(GetStorageRoot(), companyId.ToString());
        Directory.CreateDirectory(companyPath);
        var filePath = Path.Combine(companyPath, fileName);

        var entity = new DataExportHistory
        {
            CompanyId = companyId,
            ExportType = exportType,
            FileName = fileName,
            FilePath = filePath,
            FileSizeBytes = 0,
            Status = JobRunStatus.Running,
            StartedAt = startedAt,
            CreatedAt = startedAt,
            CreatedBy = userName
        };

        await _unitOfWork.Repository<DataExportHistory>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            var bytes = await BuildExportAsync(exportType, companyId, cancellationToken);
            await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);

            entity.FileSizeBytes = new FileInfo(filePath).Length;
            entity.Status = JobRunStatus.Completed;
            entity.CompletedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = userName;
            _unitOfWork.Repository<DataExportHistory>().Update(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new JobActionResult(true, $"{ToLabel(exportType)} export completed.", entity.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Data export failed for {ExportType}", exportType);
            entity.Status = JobRunStatus.Failed;
            entity.ErrorMessage = ex.Message;
            entity.CompletedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = userName;
            _unitOfWork.Repository<DataExportHistory>().Update(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return new JobActionResult(false, "Export failed: " + ex.Message, entity.Id);
        }
    }

    public async Task<(byte[] Content, string FileName)?> DownloadAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var row = await _unitOfWork.Repository<DataExportHistory>()
            .Query()
            .Where(x => x.Id == id && x.CompanyId == companyId)
            .Select(x => new { x.FilePath, x.FileName })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null || !File.Exists(row.FilePath))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(row.FilePath, cancellationToken);
        return (bytes, row.FileName);
    }

    public async Task<JobActionResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var entity = await _unitOfWork.Repository<DataExportHistory>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new JobActionResult(false, "Export record not found.");
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(entity.FilePath) && File.Exists(entity.FilePath))
            {
                File.Delete(entity.FilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete export file {Path}", entity.FilePath);
        }

        _unitOfWork.Repository<DataExportHistory>().Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return new JobActionResult(true, "Export record deleted.");
    }

    private async Task<byte[]> BuildExportAsync(
        DataExportType exportType,
        int companyId,
        CancellationToken cancellationToken)
    {
        return exportType switch
        {
            DataExportType.ChartOfAccounts => await _chartOfAccountsService.ExportToExcelAsync(cancellationToken),
            DataExportType.Customers => await ExportCustomersAsync(companyId, cancellationToken),
            DataExportType.Vendors => await ExportVendorsAsync(companyId, cancellationToken),
            DataExportType.Items => await ExportItemsAsync(companyId, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported export type.")
        };
    }

    private async Task<byte[]> ExportCustomersAsync(int companyId, CancellationToken cancellationToken)
    {
        var rows = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.BuyerId)
            .Select(x => new
            {
                x.BuyerId,
                x.BuyerName,
                x.Phone,
                x.Email,
                x.NTN,
                x.STRN,
                x.OpeningBalance,
                x.IsActive
            })
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Customers");
        ws.Cell(1, 1).Value = "Code";
        ws.Cell(1, 2).Value = "Name";
        ws.Cell(1, 3).Value = "Phone";
        ws.Cell(1, 4).Value = "Email";
        ws.Cell(1, 5).Value = "NTN";
        ws.Cell(1, 6).Value = "STRN";
        ws.Cell(1, 7).Value = "Opening Balance";
        ws.Cell(1, 8).Value = "Active";
        ws.Row(1).Style.Font.Bold = true;

        var index = 2;
        foreach (var row in rows)
        {
            ws.Cell(index, 1).Value = row.BuyerId;
            ws.Cell(index, 2).Value = row.BuyerName;
            ws.Cell(index, 3).Value = row.Phone ?? string.Empty;
            ws.Cell(index, 4).Value = row.Email ?? string.Empty;
            ws.Cell(index, 5).Value = row.NTN ?? string.Empty;
            ws.Cell(index, 6).Value = row.STRN ?? string.Empty;
            ws.Cell(index, 7).Value = row.OpeningBalance;
            ws.Cell(index, 8).Value = row.IsActive ? "Yes" : "No";
            index++;
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private async Task<byte[]> ExportVendorsAsync(int companyId, CancellationToken cancellationToken)
    {
        var rows = await _unitOfWork.Repository<Vendor>()
            .Query()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.VendorCode)
            .Select(x => new
            {
                x.VendorCode,
                x.VendorName,
                x.Phone,
                x.Email,
                x.NTN,
                x.OpeningBalance,
                x.DefaultSalesTaxRate,
                x.IsActive
            })
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Vendors");
        ws.Cell(1, 1).Value = "Code";
        ws.Cell(1, 2).Value = "Name";
        ws.Cell(1, 3).Value = "Phone";
        ws.Cell(1, 4).Value = "Email";
        ws.Cell(1, 5).Value = "NTN";
        ws.Cell(1, 6).Value = "Opening Balance";
        ws.Cell(1, 7).Value = "Default Tax %";
        ws.Cell(1, 8).Value = "Active";
        ws.Row(1).Style.Font.Bold = true;

        var index = 2;
        foreach (var row in rows)
        {
            ws.Cell(index, 1).Value = row.VendorCode;
            ws.Cell(index, 2).Value = row.VendorName;
            ws.Cell(index, 3).Value = row.Phone ?? string.Empty;
            ws.Cell(index, 4).Value = row.Email ?? string.Empty;
            ws.Cell(index, 5).Value = row.NTN ?? string.Empty;
            ws.Cell(index, 6).Value = row.OpeningBalance;
            ws.Cell(index, 7).Value = row.DefaultSalesTaxRate;
            ws.Cell(index, 8).Value = row.IsActive ? "Yes" : "No";
            index++;
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private async Task<byte[]> ExportItemsAsync(int companyId, CancellationToken cancellationToken)
    {
        var rows = await _unitOfWork.Repository<Item>()
            .Query()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.ItemCode)
            .Select(x => new
            {
                x.ItemCode,
                x.ItemName,
                x.ItemType,
                x.PurchaseRate,
                x.SaleRate,
                x.CurrentStock,
                x.MinimumStock,
                x.ReorderLevel,
                x.IsActive
            })
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Items");
        ws.Cell(1, 1).Value = "Code";
        ws.Cell(1, 2).Value = "Name";
        ws.Cell(1, 3).Value = "Type";
        ws.Cell(1, 4).Value = "Purchase Rate";
        ws.Cell(1, 5).Value = "Sale Rate";
        ws.Cell(1, 6).Value = "Current Stock";
        ws.Cell(1, 7).Value = "Minimum Stock";
        ws.Cell(1, 8).Value = "Reorder Level";
        ws.Cell(1, 9).Value = "Active";
        ws.Row(1).Style.Font.Bold = true;

        var index = 2;
        foreach (var row in rows)
        {
            ws.Cell(index, 1).Value = row.ItemCode;
            ws.Cell(index, 2).Value = row.ItemName;
            ws.Cell(index, 3).Value = row.ItemType.ToString();
            ws.Cell(index, 4).Value = row.PurchaseRate;
            ws.Cell(index, 5).Value = row.SaleRate;
            ws.Cell(index, 6).Value = row.CurrentStock;
            ws.Cell(index, 7).Value = row.MinimumStock;
            ws.Cell(index, 8).Value = row.ReorderLevel;
            ws.Cell(index, 9).Value = row.IsActive ? "Yes" : "No";
            index++;
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private string GetStorageRoot()
    {
        var raw = string.IsNullOrWhiteSpace(_options.StoragePath) ? "App_Data/Exports" : _options.StoragePath;
        return Path.IsPathRooted(raw) ? raw : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, raw));
    }

    private static string ToLabel(DataExportType value)
    {
        return value switch
        {
            DataExportType.ChartOfAccounts => "Chart of Accounts",
            DataExportType.Customers => "Customers",
            DataExportType.Vendors => "Vendors",
            DataExportType.Items => "Items",
            _ => value.ToString()
        };
    }

    private static IQueryable<DataExportHistory> ApplyOrdering(
        IQueryable<DataExportHistory> query,
        DataTableRequest request)
    {
        var desc = string.Equals(request.OrderDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return request.OrderColumn switch
        {
            0 => desc ? query.OrderByDescending(x => x.ExportType) : query.OrderBy(x => x.ExportType),
            1 => desc ? query.OrderByDescending(x => x.FileName) : query.OrderBy(x => x.FileName),
            2 => desc ? query.OrderByDescending(x => x.FileSizeBytes) : query.OrderBy(x => x.FileSizeBytes),
            3 => desc ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status),
            4 => desc ? query.OrderByDescending(x => x.StartedAt) : query.OrderBy(x => x.StartedAt),
            5 => desc ? query.OrderByDescending(x => x.CompletedAt) : query.OrderBy(x => x.CompletedAt),
            _ => query.OrderByDescending(x => x.StartedAt)
        };
    }
}
