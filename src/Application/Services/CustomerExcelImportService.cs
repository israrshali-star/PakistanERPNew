using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class CustomerExcelImportService : ICustomerExcelImportService
{
    private const int BatchSize = 200;
    private const string ImportUser = "customer-excel-import";
    private const string DefaultSheetName = "Customers";

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CustomerExcelImportService> _logger;

    public CustomerExcelImportService(IUnitOfWork unitOfWork, ILogger<CustomerExcelImportService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<CustomerExcelImportResult> ImportAsync(
        string filePath,
        int companyId,
        bool updateExisting = false,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return new CustomerExcelImportResult(false, $"Excel file not found: {filePath}");
        }

        var companyExists = await _unitOfWork.Repository<Company>()
            .Query()
            .AnyAsync(c => c.Id == companyId, cancellationToken);

        if (!companyExists)
        {
            return new CustomerExcelImportResult(false, $"Company id {companyId} was not found.");
        }

        var provinces = await _unitOfWork.Repository<Province>()
            .Query()
            .Where(p => p.IsActive)
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(cancellationToken);

        var provinceByName = provinces.ToDictionary(
            p => p.Name.Trim().ToUpperInvariant(),
            p => p.Id,
            StringComparer.OrdinalIgnoreCase);

        var existingCustomers = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        var existingByName = existingCustomers.ToDictionary(
            c => c.BuyerName.Trim().ToUpperInvariant(),
            c => c,
            StringComparer.OrdinalIgnoreCase);

        var nextBuyerNumber = await GetNextCustomerNumberAsync(companyId, cancellationToken);
        var now = DateTime.UtcNow;
        var imported = 0;
        var skipped = 0;
        var updated = 0;
        var batch = new List<Customer>();

        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.TryGetWorksheet(DefaultSheetName, out var namedSheet)
            ? namedSheet
            : workbook.Worksheets.First();

        var headerRow = worksheet.FirstRowUsed();
        if (headerRow is null)
        {
            return new CustomerExcelImportResult(false, "Excel sheet is empty.");
        }

        var columnMap = BuildColumnMap(headerRow);
        if (!columnMap.ContainsKey("customername"))
        {
            return new CustomerExcelImportResult(false, "Customers sheet must include a 'Customer Name' column.");
        }

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            var buyerName = GetCell(row, columnMap, "customername");
            if (string.IsNullOrWhiteSpace(buyerName))
            {
                continue;
            }

            var address = NullIfEmpty(GetCell(row, columnMap, "address"));
            var phone = NullIfEmpty(GetCell(row, columnMap, "phone"));
            var email = NullIfEmpty(GetCell(row, columnMap, "email"));
            var cnic = NormalizeTaxId(GetCell(row, columnMap, "cnic"));
            var ntn = NormalizeTaxId(GetCell(row, columnMap, "ntn"));
            var strn = NormalizeTaxId(GetCell(row, columnMap, "registration", "strn"));
            var provinceName = GetCell(row, columnMap, "province");
            var provinceId = ResolveProvinceId(provinceName, provinceByName);
            var (customerType, scenarioId) = ResolveCustomerTypeAndScenario(ntn, cnic);

            if (existingByName.TryGetValue(buyerName.Trim().ToUpperInvariant(), out var existing))
            {
                if (!updateExisting)
                {
                    skipped++;
                    continue;
                }

                existing.Address = address ?? existing.Address;
                existing.Phone = phone ?? existing.Phone;
                existing.Email = email ?? existing.Email;
                existing.CNIC = cnic ?? existing.CNIC;
                existing.NTN = ntn ?? existing.NTN;
                existing.STRN = strn ?? existing.STRN;
                existing.ProvinceId = provinceId ?? existing.ProvinceId;
                existing.CustomerType = customerType;
                existing.ScenarioId = scenarioId;
                existing.IsActive = true;
                existing.UpdatedAt = now;
                existing.UpdatedBy = ImportUser;
                updated++;
                continue;
            }

            batch.Add(new Customer
            {
                CompanyId = companyId,
                BuyerId = $"{AppConstants.CustomerIdPrefix}{nextBuyerNumber:D4}",
                BuyerName = buyerName.Trim(),
                OpeningBalance = 0m,
                Address = address,
                ProvinceId = provinceId,
                ScenarioId = scenarioId,
                Phone = phone,
                Email = email,
                NTN = ntn,
                CNIC = cnic,
                STRN = strn,
                CustomerType = customerType,
                InvoiceType = InvoiceType.SalesInvoice,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = ImportUser
            });

            existingByName[buyerName.Trim().ToUpperInvariant()] = batch[^1];
            nextBuyerNumber++;

            if (batch.Count >= BatchSize)
            {
                await _unitOfWork.Repository<Customer>().AddRangeAsync(batch, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                imported += batch.Count;
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await _unitOfWork.Repository<Customer>().AddRangeAsync(batch, cancellationToken);
        }

        if (imported > 0 || updated > 0 || batch.Count > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            imported += batch.Count;
        }

        var message =
            $"Imported {imported} customers, updated {updated}, skipped {skipped} for company {companyId}.";
        _logger.LogInformation(message);
        return new CustomerExcelImportResult(true, message, imported, skipped, updated);
    }

    private static Dictionary<string, int> BuildColumnMap(IXLRow headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var key = NormalizeHeader(cell.GetString());
            if (!string.IsNullOrWhiteSpace(key) && !map.ContainsKey(key))
            {
                map[key] = cell.Address.ColumnNumber;
            }
        }

        return map;
    }

    private static string NormalizeHeader(string value) =>
        string.Concat(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit));

    private static string GetCell(IXLRow row, IReadOnlyDictionary<string, int> columnMap, params string[] keys)
    {
        foreach (var key in keys)
        {
            var normalized = NormalizeHeader(key);
            if (columnMap.TryGetValue(normalized, out var column))
            {
                return row.Cell(column).GetString().Trim();
            }
        }

        return string.Empty;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeTaxId(string? value)
    {
        var trimmed = NullIfEmpty(value);
        if (trimmed is null)
        {
            return null;
        }

        if (trimmed is "0" or "0.0")
        {
            return null;
        }

        return trimmed;
    }

    private static int? ResolveProvinceId(string provinceName, IReadOnlyDictionary<string, int> provinceByName)
    {
        if (string.IsNullOrWhiteSpace(provinceName))
        {
            return 1;
        }

        var key = provinceName.Trim().ToUpperInvariant();
        if (provinceByName.TryGetValue(key, out var id))
        {
            return id;
        }

        if (key.Contains("PUNJAB", StringComparison.Ordinal))
        {
            return provinceByName.GetValueOrDefault("PUNJAB", 1);
        }

        if (key.Contains("SINDH", StringComparison.Ordinal))
        {
            return provinceByName.GetValueOrDefault("SINDH", 2);
        }

        if (key.Contains("KHYBER", StringComparison.Ordinal) || key.Contains("KPK", StringComparison.Ordinal))
        {
            return provinceByName.GetValueOrDefault("KHYBER PAKHTUNKHWA", 3);
        }

        return 1;
    }

    private static (CustomerType CustomerType, int ScenarioId) ResolveCustomerTypeAndScenario(string? ntn, string? cnic)
    {
        if (!string.IsNullOrWhiteSpace(ntn))
        {
            return (CustomerType.Registered, 1);
        }

        if (!string.IsNullOrWhiteSpace(cnic))
        {
            return (CustomerType.Unregistered, 2);
        }

        return (CustomerType.Registered, 1);
    }

    private async Task<int> GetNextCustomerNumberAsync(int companyId, CancellationToken cancellationToken)
    {
        var prefix = AppConstants.CustomerIdPrefix;
        var buyerIds = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId && c.BuyerId.StartsWith(prefix))
            .Select(c => c.BuyerId)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var buyerId in buyerIds)
        {
            if (buyerId.Length > prefix.Length
                && int.TryParse(buyerId[prefix.Length..], out var number))
            {
                max = Math.Max(max, number);
            }
        }

        return max + 1;
    }

    public async Task<CustomerExcelImportResult> FixDuplicateNameInAddressesAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        var customers = await _unitOfWork.Repository<Customer>()
            .Query(asNoTracking: false)
            .Where(c => c.CompanyId == companyId && c.Address != null && c.Address != "")
            .ToListAsync(cancellationToken);

        var updated = 0;
        var now = DateTime.UtcNow;

        foreach (var customer in customers)
        {
            var cleaned = CustomerAddressHelper.RemoveLeadingBuyerName(customer.BuyerName, customer.Address);
            if (string.Equals(cleaned ?? string.Empty, customer.Address ?? string.Empty, StringComparison.Ordinal))
            {
                continue;
            }

            customer.Address = cleaned;
            customer.UpdatedAt = now;
            customer.UpdatedBy = "address-fix";
            _unitOfWork.Repository<Customer>().Update(customer);
            updated++;
        }

        if (updated > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new CustomerExcelImportResult(
            true,
            $"Removed duplicate buyer name from {updated} customer address(es).",
            Updated: updated);
    }
}
