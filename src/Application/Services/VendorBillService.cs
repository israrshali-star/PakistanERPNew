using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common.Constants;
using static PakistanAccountingERP.Application.Common.Constants.GlAccountNumbers;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;
using System.Text.RegularExpressions;

namespace PakistanAccountingERP.Application.Services;

public partial class VendorBillService : IVendorBillService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly IItemCartonSyncService _itemCartonSyncService;
    private readonly ILogger<VendorBillService> _logger;

    public VendorBillService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IAuditService auditService,
        IItemCartonSyncService itemCartonSyncService,
        ILogger<VendorBillService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _auditService = auditService;
        _itemCartonSyncService = itemCartonSyncService;
        _logger = logger;
    }

    public async Task<DataTableResponse<VendorBillListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var query = _unitOfWork.Repository<VendorBill>()
            .Query()
            .Where(b => b.CompanyId == companyId);

        var recordsTotal = await query.CountAsync(cancellationToken);

        if (fromDate.HasValue)
        {
            var from = fromDate.Value.Date;
            query = query.Where(b => b.BillDate >= from);
        }

        if (toDate.HasValue)
        {
            var to = toDate.Value.Date.AddDays(1);
            query = query.Where(b => b.BillDate < to);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var term = request.SearchValue.Trim();
            query = query.Where(b =>
                b.BillNumber.Contains(term)
                || b.Vendor.VendorName.Contains(term)
                || b.Vendor.VendorCode.Contains(term)
                || (b.RefNo != null && b.RefNo.Contains(term)));
        }

        var recordsFiltered = await query.CountAsync(cancellationToken);
        query = ApplyOrdering(query, request);

        if (request.Length > 0)
        {
            query = query.Skip(request.Start).Take(request.Length);
        }

        var rows = await query
            .Select(b => new VendorBillListItemDto(
                b.Id,
                b.BillNumber,
                b.Vendor.VendorName,
                b.BillDate,
                b.NetAmount,
                b.Status.ToString(),
                b.Status == BillStatus.Draft,
                b.Status == BillStatus.Draft,
                b.Status == BillStatus.Draft,
                b.Status != BillStatus.Cancelled))
            .ToListAsync(cancellationToken);

        return new DataTableResponse<VendorBillListItemDto>(
            request.Draw,
            recordsTotal,
            recordsFiltered,
            rows);
    }

    public async Task<VendorBillDetailDto?> GetDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var bill = await _unitOfWork.Repository<VendorBill>()
            .Query()
            .Where(b => b.Id == id && b.CompanyId == companyId)
            .Select(b => new
            {
                b.Id,
                b.BillNumber,
                b.RefNo,
                b.VendorId,
                b.Vendor.VendorCode,
                b.Vendor.VendorName,
                b.BillDate,
                b.WarehouseId,
                WarehouseCode = b.Warehouse != null ? b.Warehouse.Code : null,
                WarehouseName = b.Warehouse != null ? b.Warehouse.Name : null,
                b.TaxAmount,
                b.NetAmount,
                b.TotalQuantity,
                b.TotalCartons,
                b.Status,
                b.JournalEntryId,
                JournalEntryNumber = b.JournalEntry != null ? b.JournalEntry.EntryNumber : null,
                Lines = b.Lines.Select(l => new VendorBillLineDto(
                    l.Id,
                    l.ItemId,
                    l.Item != null ? l.Item.ItemCode : null,
                    l.Item != null ? l.Item.ItemName : null,
                    l.Description,
                    l.StackNo,
                    l.LotNo,
                    l.Quantity,
                    l.Cartons,
                    l.Rate,
                    l.Amount)).ToList(),
                Attachments = b.Attachments.Select(a => new DocumentAttachmentDto(
                    a.Id,
                    a.FileName,
                    a.ContentType,
                    a.FileSizeBytes,
                    a.CreatedAt,
                    a.CreatedBy)).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (bill is null)
        {
            return null;
        }

        var subTotal = Math.Round(bill.NetAmount - bill.TaxAmount, 2);

        return new VendorBillDetailDto(
            bill.Id,
            bill.BillNumber,
            bill.RefNo,
            bill.VendorId,
            bill.VendorCode,
            bill.VendorName,
            bill.BillDate,
            bill.WarehouseId,
            bill.WarehouseCode,
            bill.WarehouseName,
            subTotal,
            bill.TaxAmount,
            bill.NetAmount,
            bill.TotalQuantity,
            bill.TotalCartons,
            bill.Status,
            bill.JournalEntryId,
            bill.JournalEntryNumber,
            bill.Lines,
            bill.Attachments);
    }

    public async Task<NextVendorBillNumberDto> GenerateNextBillNumberAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var prefix = AppConstants.VendorBillNumberPrefix;

        var numbers = await _unitOfWork.Repository<VendorBill>()
            .Query()
            .Where(b => b.CompanyId == companyId && b.BillNumber.StartsWith(prefix))
            .Select(b => b.BillNumber)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var number in numbers)
        {
            var match = BillNumberRegex().Match(number);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var seq))
            {
                max = Math.Max(max, seq);
            }
        }

        return new NextVendorBillNumberDto($"{prefix}{(max + 1):D4}");
    }

    public async Task<IReadOnlyList<VendorBillVendorLookupDto>> GetVendorLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<Vendor>()
            .Query()
            .Where(v => v.CompanyId == companyId && v.IsActive)
            .OrderBy(v => v.VendorName)
            .Select(v => new VendorBillVendorLookupDto(
                v.Id,
                v.VendorCode,
                v.VendorName,
                v.DefaultSalesTaxRate))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VendorBillItemLookupDto>> GetItemLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId && i.IsActive)
            .OrderBy(i => i.ItemName)
            .Select(i => new VendorBillItemLookupDto(
                i.Id,
                i.ItemCode,
                i.ItemName,
                i.Description,
                i.StackNo,
                i.LotNo,
                i.PurchaseRate))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VendorBillWarehouseLookupDto>> GetWarehouseLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<Warehouse>()
            .Query()
            .Where(w => w.CompanyId == companyId && w.IsActive)
            .OrderBy(w => w.Name)
            .Select(w => new VendorBillWarehouseLookupDto(w.Id, w.Code, w.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<VendorBillSaveResult> CreateAsync(
        VendorBillSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        if (request.VendorId <= 0)
        {
            return new VendorBillSaveResult(false, "Vendor is required.", null);
        }

        if (request.Lines.Count == 0)
        {
            return new VendorBillSaveResult(false, "Add at least one bill line.", null);
        }

        var vendor = await _unitOfWork.Repository<Vendor>()
            .Query()
            .FirstOrDefaultAsync(v => v.Id == request.VendorId && v.CompanyId == companyId, cancellationToken);

        if (vendor is null)
        {
            return new VendorBillSaveResult(false, "Vendor not found.", null);
        }

        var billNumber = string.IsNullOrWhiteSpace(request.BillNumber)
            ? (await GenerateNextBillNumberAsync(cancellationToken)).BillNumber
            : request.BillNumber.Trim();

        var numberExists = await _unitOfWork.Repository<VendorBill>()
            .Query()
            .AnyAsync(b => b.CompanyId == companyId && b.BillNumber == billNumber, cancellationToken);

        if (numberExists)
        {
            return new VendorBillSaveResult(false, "Bill number already exists.", null);
        }

        var lineBuild = await BuildLineEntitiesAsync(request.Lines, companyId, cancellationToken);
        if (!lineBuild.Success)
        {
            return new VendorBillSaveResult(false, lineBuild.Message, null);
        }

        var warehouseValidation = await ValidateWarehouseAsync(
            request.WarehouseId,
            HasInventoryItemLines(request.Lines),
            companyId,
            cancellationToken);
        if (!warehouseValidation.Success)
        {
            return new VendorBillSaveResult(false, warehouseValidation.Message, null);
        }

        var subTotal = Math.Round(lineBuild.Lines.Sum(l => l.Amount), 2);
        var taxRate = ResolveBillTaxRate(request.TaxRate, vendor.DefaultSalesTaxRate);
        var taxAmount = Math.Round(lineBuild.TaxableSubTotal * Math.Max(0m, taxRate) / 100m, 2);
        var netAmount = Math.Round(subTotal + taxAmount, 2);
        var now = DateTime.UtcNow;

        var entity = new VendorBill
        {
            CompanyId = companyId,
            VendorId = vendor.Id,
            WarehouseId = request.WarehouseId,
            BillNumber = billNumber,
            RefNo = request.RefNo?.Trim(),
            BillDate = request.BillDate.Date,
            TotalQuantity = lineBuild.Lines.Sum(l => l.Quantity),
            TotalCartons = lineBuild.Lines.Sum(l => l.Cartons),
            TaxAmount = taxAmount,
            NetAmount = netAmount,
            Status = BillStatus.Draft,
            CreatedAt = now,
            CreatedBy = _currentUser.UserName ?? "system"
        };

        try
        {
            await _unitOfWork.Repository<VendorBill>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            foreach (var line in lineBuild.Lines)
            {
                line.VendorBillId = entity.Id;
            }

            await _unitOfWork.Repository<VendorBillLine>().AddRangeAsync(lineBuild.Lines, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to create vendor bill {BillNumber}", billNumber);
            return new VendorBillSaveResult(false, "Could not save vendor bill.", null);
        }

        try
        {
            await _auditService.LogAsync("Create", "VendorBills", entity.Id.ToString(), null, billNumber, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for vendor bill {BillId}", entity.Id);
        }

        return new VendorBillSaveResult(true, null, entity.Id);
    }

    public async Task<VendorBillSaveResult> UpdateAsync(
        VendorBillSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.Id.HasValue || request.Id.Value <= 0)
        {
            return new VendorBillSaveResult(false, "Bill id is required.", null);
        }

        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        if (request.VendorId <= 0)
        {
            return new VendorBillSaveResult(false, "Vendor is required.", null);
        }

        if (request.Lines.Count == 0)
        {
            return new VendorBillSaveResult(false, "Add at least one bill line.", null);
        }

        var entity = await _unitOfWork.Repository<VendorBill>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(b => b.Id == request.Id.Value && b.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new VendorBillSaveResult(false, "Vendor bill not found.", null);
        }

        if (entity.Status != BillStatus.Draft)
        {
            return new VendorBillSaveResult(false, "Only draft bills can be edited.", null);
        }

        var vendor = await _unitOfWork.Repository<Vendor>()
            .Query()
            .FirstOrDefaultAsync(v => v.Id == request.VendorId && v.CompanyId == companyId, cancellationToken);

        if (vendor is null)
        {
            return new VendorBillSaveResult(false, "Vendor not found.", null);
        }

        var billNumber = string.IsNullOrWhiteSpace(request.BillNumber)
            ? entity.BillNumber
            : request.BillNumber.Trim();

        var numberExists = await _unitOfWork.Repository<VendorBill>()
            .Query()
            .AnyAsync(b => b.CompanyId == companyId
                           && b.BillNumber == billNumber
                           && b.Id != entity.Id, cancellationToken);

        if (numberExists)
        {
            return new VendorBillSaveResult(false, "Bill number already exists.", null);
        }

        var lineBuild = await BuildLineEntitiesAsync(request.Lines, companyId, cancellationToken);
        if (!lineBuild.Success)
        {
            return new VendorBillSaveResult(false, lineBuild.Message, null);
        }

        var warehouseValidation = await ValidateWarehouseAsync(
            request.WarehouseId,
            HasInventoryItemLines(request.Lines),
            companyId,
            cancellationToken);
        if (!warehouseValidation.Success)
        {
            return new VendorBillSaveResult(false, warehouseValidation.Message, null);
        }

        var existingLines = await _unitOfWork.Repository<VendorBillLine>()
            .Query(asNoTracking: false)
            .Where(l => l.VendorBillId == entity.Id)
            .ToListAsync(cancellationToken);

        foreach (var existingLine in existingLines)
        {
            _unitOfWork.Repository<VendorBillLine>().Remove(existingLine);
        }

        var subTotal = Math.Round(lineBuild.Lines.Sum(l => l.Amount), 2);
        var taxRate = ResolveBillTaxRate(request.TaxRate, vendor.DefaultSalesTaxRate);
        var taxAmount = Math.Round(lineBuild.TaxableSubTotal * Math.Max(0m, taxRate) / 100m, 2);
        var netAmount = Math.Round(subTotal + taxAmount, 2);
        var now = DateTime.UtcNow;

        entity.VendorId = vendor.Id;
        entity.WarehouseId = request.WarehouseId;
        entity.BillNumber = billNumber;
        entity.RefNo = request.RefNo?.Trim();
        entity.BillDate = request.BillDate.Date;
        entity.TotalQuantity = lineBuild.Lines.Sum(l => l.Quantity);
        entity.TotalCartons = lineBuild.Lines.Sum(l => l.Cartons);
        entity.TaxAmount = taxAmount;
        entity.NetAmount = netAmount;
        entity.UpdatedAt = now;
        entity.UpdatedBy = _currentUser.UserName ?? "system";

        try
        {
            _unitOfWork.Repository<VendorBill>().Update(entity);

            foreach (var line in lineBuild.Lines)
            {
                line.VendorBillId = entity.Id;
            }

            await _unitOfWork.Repository<VendorBillLine>().AddRangeAsync(lineBuild.Lines, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to update vendor bill {BillId}", entity.Id);
            return new VendorBillSaveResult(false, "Could not update vendor bill.", null);
        }

        try
        {
            await _auditService.LogAsync("Update", "VendorBills", entity.Id.ToString(), null, billNumber, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for vendor bill {BillId}", entity.Id);
        }

        return new VendorBillSaveResult(true, null, entity.Id);
    }

    public async Task<VendorBillActionResult> ApproveAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return ToActionError(companyError!);
        }

        var bill = await _unitOfWork.Repository<VendorBill>()
            .Query(asNoTracking: false)
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.Id == id && b.CompanyId == companyId, cancellationToken);

        if (bill is null)
        {
            return new VendorBillActionResult(false, "Vendor bill not found.", null);
        }

        if (bill.Status != BillStatus.Draft)
        {
            return new VendorBillActionResult(false, "Only draft bills can be approved.", null);
        }

        if (bill.Lines.Count == 0)
        {
            return new VendorBillActionResult(false, "Bill has no line items.", null);
        }

        var accounts = await ResolvePostingAccountsAsync(companyId, cancellationToken);
        if (!accounts.Success)
        {
            return new VendorBillActionResult(false, accounts.Message, null);
        }

        var subTotal = Math.Round(bill.NetAmount - bill.TaxAmount, 2);
        var taxAmount = Math.Round(bill.TaxAmount, 2);
        var netAmount = Math.Round(bill.NetAmount, 2);

        if (subTotal + taxAmount != netAmount)
        {
            return new VendorBillActionResult(false, "Bill totals are inconsistent. Cannot approve.", null);
        }

        if (subTotal <= 0m)
        {
            return new VendorBillActionResult(false, "Bill subtotal must be greater than zero.", null);
        }

        var itemLineIds = bill.Lines
            .Where(l => l.ItemId.HasValue && l.ItemId > 0)
            .Select(l => l.ItemId!.Value)
            .Distinct()
            .ToList();

        int? warehouseId = null;
        Dictionary<int, Item> items = [];

        if (itemLineIds.Count > 0)
        {
            if (!bill.WarehouseId.HasValue)
            {
                return new VendorBillActionResult(
                    false,
                    "Warehouse is required before approving bills with inventory items.",
                    null);
            }

            warehouseId = bill.WarehouseId;
            var warehouseValid = await _unitOfWork.Repository<Warehouse>()
                .Query()
                .AnyAsync(
                    w => w.Id == warehouseId.Value && w.CompanyId == companyId && w.IsActive,
                    cancellationToken);
            if (!warehouseValid)
            {
                return new VendorBillActionResult(
                    false,
                    "Selected warehouse is invalid or inactive.",
                    null);
            }

            items = await _unitOfWork.Repository<Item>()
                .Query(asNoTracking: false)
                .Where(i => i.CompanyId == companyId && itemLineIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, cancellationToken);

            if (items.Count != itemLineIds.Count)
            {
                return new VendorBillActionResult(false, "One or more bill items are invalid.", null);
            }
        }

        var journalLines = new List<JournalEntryLine>();
        AddJournalLine(journalLines, accounts.InventoryAccountId, subTotal, 0m, "Inventory Asset");
        AddJournalLine(journalLines, accounts.InputTaxAccountId, taxAmount, 0m, "Pre Paid Sales Tax");
        AddJournalLine(journalLines, accounts.PayableAccountId, 0m, netAmount, "Account Payable");

        var entryNumber = await GenerateNextJournalEntryNumberAsync(companyId, cancellationToken);
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "system";

        var journalEntry = new JournalEntry
        {
            CompanyId = companyId,
            EntryNumber = entryNumber,
            EntryDate = bill.BillDate,
            Description = $"Vendor bill {bill.BillNumber}",
            ReferenceType = ReferenceTypes.VendorBill,
            ReferenceId = bill.Id,
            Status = JournalStatus.Posted,
            CreatedAt = now,
            CreatedBy = userName
        };

        try
        {
            await _unitOfWork.Repository<JournalEntry>().AddAsync(journalEntry, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            foreach (var line in journalLines)
            {
                line.JournalEntryId = journalEntry.Id;
            }

            await _unitOfWork.Repository<JournalEntryLine>().AddRangeAsync(journalLines, cancellationToken);

            if (warehouseId.HasValue)
            {
                var hasExistingInventory = await _unitOfWork.Repository<InventoryTransaction>()
                    .Query()
                    .AnyAsync(
                        t => t.CompanyId == companyId && t.ReferenceNo == bill.BillNumber,
                        cancellationToken);

                if (!hasExistingInventory)
                {
                    var inventoryTransactions = new List<InventoryTransaction>();
                    foreach (var line in bill.Lines.Where(l => l.ItemId.HasValue && l.ItemId > 0))
                    {
                        var item = items[line.ItemId!.Value];
                        var quantity = Math.Round(line.Quantity, 2);
                        if (quantity <= 0m)
                        {
                            continue;
                        }

                        var unitCost = Math.Round(line.Rate, 2);
                        inventoryTransactions.Add(new InventoryTransaction
                        {
                            CompanyId = companyId,
                            ItemId = item.Id,
                            WarehouseId = warehouseId.Value,
                            TransactionType = InventoryTransactionType.StockIn,
                            StackNo = string.IsNullOrWhiteSpace(line.StackNo) ? null : line.StackNo.Trim(),
                            LotNo = string.IsNullOrWhiteSpace(line.LotNo) ? null : line.LotNo.Trim(),
                            Quantity = quantity,
                            UnitCost = unitCost,
                            TotalCost = Math.Round(quantity * unitCost, 2),
                            TransactionDate = bill.BillDate,
                            ReferenceNo = bill.BillNumber,
                            Notes = $"Vendor bill {bill.BillNumber}",
                            CreatedAt = now,
                            CreatedBy = userName
                        });

                        item.CurrentStock = Math.Round(item.CurrentStock + quantity, 2);
                        item.PurchaseRate = unitCost;
                        item.UpdatedAt = now;
                        item.UpdatedBy = userName;
                        _unitOfWork.Repository<Item>().Update(item);
                    }

                    if (inventoryTransactions.Count > 0)
                    {
                        await _unitOfWork.Repository<InventoryTransaction>().AddRangeAsync(
                            inventoryTransactions,
                            cancellationToken);
                    }
                }
            }

            bill.Status = BillStatus.Approved;
            bill.JournalEntryId = journalEntry.Id;
            bill.UpdatedAt = now;
            bill.UpdatedBy = userName;

            _unitOfWork.Repository<VendorBill>().Update(bill);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (itemLineIds.Count > 0)
            {
                await _itemCartonSyncService.SyncItemsAsync(companyId, itemLineIds, cancellationToken);
            }
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to approve vendor bill {BillId}", id);
            return new VendorBillActionResult(false, "Could not post bill to the general ledger.", null);
        }

        try
        {
            await _auditService.LogAsync(
                "Approve",
                "VendorBills",
                id.ToString(),
                BillStatus.Draft.ToString(),
                BillStatus.Approved.ToString(),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for vendor bill {BillId}", id);
        }

        var detail = await GetDetailAsync(id, cancellationToken);
        return new VendorBillActionResult(true, "Vendor bill approved and posted to the general ledger.", detail);
    }

    public async Task<VendorBillActionResult> CancelAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return ToActionError(companyError!);
        }

        var bill = await _unitOfWork.Repository<VendorBill>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(b => b.Id == id && b.CompanyId == companyId, cancellationToken);

        if (bill is null)
        {
            return new VendorBillActionResult(false, "Vendor bill not found.", null);
        }

        if (bill.Status != BillStatus.Draft)
        {
            return new VendorBillActionResult(false, "Only draft bills can be cancelled.", null);
        }

        bill.Status = BillStatus.Cancelled;
        bill.UpdatedAt = DateTime.UtcNow;
        bill.UpdatedBy = _currentUser.UserName;

        _unitOfWork.Repository<VendorBill>().Update(bill);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await _auditService.LogAsync(
                "Cancel",
                "VendorBills",
                id.ToString(),
                BillStatus.Draft.ToString(),
                BillStatus.Cancelled.ToString(),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for vendor bill {BillId}", id);
        }

        var detail = await GetDetailAsync(id, cancellationToken);
        return new VendorBillActionResult(true, "Vendor bill cancelled.", detail);
    }

    public async Task<VendorBillActionResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return ToActionError(companyError!);
        }

        var bill = await _unitOfWork.Repository<VendorBill>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(b => b.Id == id && b.CompanyId == companyId, cancellationToken);

        if (bill is null)
        {
            return new VendorBillActionResult(false, "Vendor bill not found.", null);
        }

        if (bill.Status != BillStatus.Draft)
        {
            return new VendorBillActionResult(false, "Only draft bills can be deleted.", null);
        }

        bill.IsDeleted = true;
        bill.DeletedAt = DateTime.UtcNow;
        bill.DeletedBy = _currentUser.UserName;

        _unitOfWork.Repository<VendorBill>().Update(bill);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await _auditService.LogAsync(
                "Delete",
                "VendorBills",
                id.ToString(),
                bill.BillNumber,
                null,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for vendor bill {BillId}", id);
        }

        return new VendorBillActionResult(true, "Vendor bill deleted.", null);
    }

    private async Task<(bool Success, string? Message, List<VendorBillLine> Lines, decimal TaxableSubTotal)> BuildLineEntitiesAsync(
        IReadOnlyList<VendorBillLineSaveRequest> lines,
        int companyId,
        CancellationToken cancellationToken)
    {
        var itemIds = lines.Where(l => l.ItemId.HasValue && l.ItemId > 0)
            .Select(l => l.ItemId!.Value)
            .Distinct()
            .ToList();

        var items = itemIds.Count == 0
            ? new Dictionary<int, (string Code, string Name, string? Description, string StackNo, string LotNo, ItemType ItemType)>()
            : await _unitOfWork.Repository<Item>()
                .Query()
                .Where(i => i.CompanyId == companyId && itemIds.Contains(i.Id))
                .Select(i => new { i.Id, i.ItemCode, i.ItemName, i.Description, i.StackNo, i.LotNo, i.ItemType })
                .ToDictionaryAsync(
                    i => i.Id,
                    i => (Code: i.ItemCode, Name: i.ItemName, Description: i.Description, StackNo: i.StackNo, LotNo: i.LotNo, ItemType: i.ItemType),
                    cancellationToken);

        if (items.Count != itemIds.Count)
        {
            return (false, "One or more items are invalid.", [], 0m);
        }

        var entities = new List<VendorBillLine>();
        decimal taxableSubTotal = 0m;

        foreach (var line in lines)
        {
            if (line.Quantity <= 0 || line.Rate < 0)
            {
                return (false, "Each line needs a positive quantity and non-negative rate.", [], 0m);
            }

            string? description = line.Description?.Trim();
            int? itemId = line.ItemId > 0 ? line.ItemId : null;

            if (itemId.HasValue)
            {
                var item = items[itemId.Value];
                description = string.IsNullOrWhiteSpace(description)
                    ? (!string.IsNullOrWhiteSpace(item.Description) ? item.Description.Trim() : item.Name)
                    : description;
            }
            else if (string.IsNullOrWhiteSpace(description))
            {
                return (false, "Each line needs an item or a description.", [], 0m);
            }

            var amount = Math.Round(line.Quantity * line.Rate, 2);
            var isTaxable = !itemId.HasValue || items[itemId.Value].ItemType != ItemType.Service;
            if (isTaxable)
            {
                taxableSubTotal += amount;
            }

            string? stackNo = line.StackNo?.Trim();
            string? lotNo = line.LotNo?.Trim();
            if (itemId.HasValue)
            {
                var item = items[itemId.Value];
                if (string.IsNullOrWhiteSpace(stackNo))
                {
                    stackNo = item.StackNo;
                }

                if (string.IsNullOrWhiteSpace(lotNo))
                {
                    lotNo = item.LotNo;
                }
            }

            entities.Add(new VendorBillLine
            {
                ItemId = itemId,
                Description = description,
                StackNo = string.IsNullOrWhiteSpace(stackNo) ? null : stackNo,
                LotNo = string.IsNullOrWhiteSpace(lotNo) ? null : lotNo,
                Quantity = line.Quantity,
                Cartons = Math.Max(0m, line.Cartons),
                Rate = line.Rate,
                Amount = amount
            });
        }

        return (true, null, entities, Math.Round(taxableSubTotal, 2));
    }

    private static bool HasInventoryItemLines(IReadOnlyList<VendorBillLineSaveRequest> lines) =>
        lines.Any(l => l.ItemId.HasValue && l.ItemId > 0);

    private async Task<(bool Success, string? Message)> ValidateWarehouseAsync(
        int? warehouseId,
        bool hasInventoryItemLines,
        int companyId,
        CancellationToken cancellationToken)
    {
        if (!hasInventoryItemLines)
        {
            return (true, null);
        }

        if (!warehouseId.HasValue || warehouseId.Value <= 0)
        {
            return (false, "Warehouse is required for bills with inventory items.");
        }

        var exists = await _unitOfWork.Repository<Warehouse>()
            .Query()
            .AnyAsync(
                w => w.Id == warehouseId.Value && w.CompanyId == companyId && w.IsActive,
                cancellationToken);

        return exists
            ? (true, null)
            : (false, "Selected warehouse is invalid or inactive.");
    }

    private async Task<(bool Success, string? Message, int PayableAccountId, int InputTaxAccountId, int InventoryAccountId)>
        ResolvePostingAccountsAsync(int companyId, CancellationToken cancellationToken)
    {
        var payable = await GetAccountIdAsync(companyId, AccountsPayable, cancellationToken);
        var inputTax = await GetAccountIdAsync(companyId, PrepaidSalesTax, cancellationToken);
        var inventory = await GetAccountIdAsync(companyId, InventoryAsset, cancellationToken);

        if (payable is null)
        {
            return (false, $"Chart of account {AccountsPayable} (Account Payable) not found.", 0, 0, 0);
        }

        if (inputTax is null)
        {
            return (false, $"Chart of account {PrepaidSalesTax} (Pre Paid Sales Tax) not found.", 0, 0, 0);
        }

        if (inventory is null)
        {
            return (false, $"Chart of account {InventoryAsset} (Inventory Asset) not found.", 0, 0, 0);
        }

        return (true, null, payable.Value, inputTax.Value, inventory.Value);
    }

    private static void AddJournalLine(
        ICollection<JournalEntryLine> lines,
        int accountId,
        decimal debit,
        decimal credit,
        string memo)
    {
        debit = Math.Round(debit, 2);
        credit = Math.Round(credit, 2);

        if (debit == 0m && credit == 0m)
        {
            return;
        }

        lines.Add(new JournalEntryLine
        {
            ChartOfAccountId = accountId,
            Debit = debit,
            Credit = credit,
            Memo = memo
        });
    }

    private async Task<int?> GetAccountIdAsync(
        int companyId,
        string accountNumber,
        CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.AccountNumber == accountNumber && a.IsActive)
            .Select(a => (int?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<string> GenerateNextJournalEntryNumberAsync(
        int companyId,
        CancellationToken cancellationToken)
    {
        var prefix = AppConstants.JournalEntryNumberPrefix;

        var numbers = await _unitOfWork.Repository<JournalEntry>()
            .Query()
            .Where(j => j.CompanyId == companyId && j.EntryNumber.StartsWith(prefix))
            .Select(j => j.EntryNumber)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var number in numbers)
        {
            var match = JournalEntryNumberRegex().Match(number);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var seq))
            {
                max = Math.Max(max, seq);
            }
        }

        return $"{prefix}{(max + 1):D4}";
    }

    private static VendorBillActionResult ToActionError(VendorBillSaveResult error) =>
        new(error.Success, error.Message, null);

    private bool TryGetCompanyId(out int companyId, out VendorBillSaveResult? error)
    {
        if (!_currentCompany.CompanyId.HasValue)
        {
            companyId = 0;
            error = new VendorBillSaveResult(
                false,
                "No company is selected. Please choose a company from the top navbar.",
                null);
            return false;
        }

        companyId = _currentCompany.CompanyId.Value;
        error = null;
        return true;
    }

    private static IQueryable<VendorBill> ApplyOrdering(IQueryable<VendorBill> query, DataTableRequest request)
    {
        var desc = string.Equals(request.OrderDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return request.OrderColumn switch
        {
            0 => desc ? query.OrderByDescending(b => b.BillNumber) : query.OrderBy(b => b.BillNumber),
            1 => desc ? query.OrderByDescending(b => b.Vendor.VendorName) : query.OrderBy(b => b.Vendor.VendorName),
            2 => desc ? query.OrderByDescending(b => b.BillDate) : query.OrderBy(b => b.BillDate),
            3 => desc ? query.OrderByDescending(b => b.NetAmount) : query.OrderBy(b => b.NetAmount),
            4 => desc ? query.OrderByDescending(b => b.Status) : query.OrderBy(b => b.Status),
            _ => desc ? query.OrderByDescending(b => b.BillDate) : query.OrderBy(b => b.BillDate)
        };
    }

    private static decimal ResolveBillTaxRate(decimal? requestTaxRate, decimal vendorDefaultTaxRate)
    {
        if (requestTaxRate is > 0m)
        {
            return requestTaxRate.Value;
        }

        return vendorDefaultTaxRate > 0m ? vendorDefaultTaxRate : 18m;
    }

    [GeneratedRegex(@"^BILL-(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex BillNumberRegex();

    [GeneratedRegex(@"^JE-(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex JournalEntryNumberRegex();
}
