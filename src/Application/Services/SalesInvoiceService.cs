using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.Common.Constants;
using static PakistanAccountingERP.Application.Common.Constants.GlAccountNumbers;
using System.Text.Json;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;
using System.Text.RegularExpressions;

namespace PakistanAccountingERP.Application.Services;

public partial class SalesInvoiceService : ISalesInvoiceService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly IFbrSubmissionService _fbrSubmissionService;
    private readonly IStackLotInventoryService _stackLotInventory;
    private readonly IInventoryCostingService _inventoryCosting;
    private readonly IItemCartonSyncService _itemCartonSyncService;
    private readonly ISalesInvoicePdfService _salesInvoicePdfService;
    private readonly ILogger<SalesInvoiceService> _logger;

    private const string CartageItemCode = "ITEM-0002";

    public SalesInvoiceService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IAuditService auditService,
        IFbrSubmissionService fbrSubmissionService,
        IStackLotInventoryService stackLotInventory,
        IInventoryCostingService inventoryCosting,
        IItemCartonSyncService itemCartonSyncService,
        ISalesInvoicePdfService salesInvoicePdfService,
        ILogger<SalesInvoiceService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _auditService = auditService;
        _fbrSubmissionService = fbrSubmissionService;
        _stackLotInventory = stackLotInventory;
        _inventoryCosting = inventoryCosting;
        _itemCartonSyncService = itemCartonSyncService;
        _salesInvoicePdfService = salesInvoicePdfService;
        _logger = logger;
    }

    public async Task<DataTableResponse<SalesInvoiceListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var query = _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(i => i.CompanyId == companyId);

        var recordsTotal = await query.CountAsync(cancellationToken);

        if (fromDate.HasValue)
        {
            var from = fromDate.Value.Date;
            query = query.Where(i => i.InvoiceDate >= from);
        }

        if (toDate.HasValue)
        {
            var to = toDate.Value.Date.AddDays(1);
            query = query.Where(i => i.InvoiceDate < to);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var term = request.SearchValue.Trim();
            query = query.Where(i =>
                i.InvoiceNumber.Contains(term)
                || i.Customer.BuyerName.Contains(term)
                || i.Customer.BuyerId.Contains(term));
        }

        var recordsFiltered = await query.CountAsync(cancellationToken);
        query = ApplyOrdering(query, request);

        if (request.Length > 0)
        {
            query = query.Skip(request.Start).Take(request.Length);
        }

        var rows = await query
            .Select(i => new SalesInvoiceListItemDto(
                i.Id,
                i.InvoiceNumber,
                i.CustomerId,
                i.Customer.BuyerName,
                i.InvoiceDate,
                i.NetTotal,
                i.Status.ToString(),
                i.FbrInvoiceNumber,
                i.Status == InvoiceStatus.Draft,
                i.Status == InvoiceStatus.Draft,
                i.Status == InvoiceStatus.Posted && i.FbrSubmittedAt == null,
                i.FbrSubmittedAt != null,
                i.FbrSubmittedAt == null,
                i.Status != InvoiceStatus.Cancelled,
                i.Status == InvoiceStatus.Posted
                    && (i.FbrSubmittedAt != null
                        || companyId == TradeInvoiceLayout.TradeInvoiceCompanyId)))
            .ToListAsync(cancellationToken);

        return new DataTableResponse<SalesInvoiceListItemDto>(
            request.Draw,
            recordsTotal,
            recordsFiltered,
            rows);
    }

    public async Task<NextInvoiceNumberDto> GenerateNextInvoiceNumberAsync(CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var prefix = AppConstants.InvoiceNumberPrefix;

        var numbers = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(i => i.CompanyId == companyId && i.InvoiceNumber.StartsWith(prefix))
            .Select(i => i.InvoiceNumber)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var number in numbers)
        {
            var match = InvoiceNumberRegex().Match(number);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var seq))
            {
                max = Math.Max(max, seq);
            }
        }

        return new NextInvoiceNumberDto($"{prefix}{(max + 1):D4}");
    }

    public async Task<IReadOnlyList<SalesInvoiceCustomerLookupDto>> GetCustomerLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId && c.IsActive)
            .OrderBy(c => c.BuyerName)
            .Select(c => new SalesInvoiceCustomerLookupDto(
                c.Id,
                c.BuyerId,
                c.BuyerName,
                c.ScenarioId,
                c.ProvinceId,
                c.Province != null ? c.Province.Name : null,
                c.Address,
                c.NTN,
                c.CNIC,
                c.InvoiceType,
                c.FurtherTaxRate))
            .ToListAsync(cancellationToken);
    }

    private const string UnregisteredScenarioCode = "SN002";

    public async Task<SalesInvoiceTaxRatesDto> GetTaxRatesAsync(CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var taxRates = await GetCompanyTaxRatesAsync(companyId, cancellationToken);
        return new SalesInvoiceTaxRatesDto(
            taxRates.Registered,
            taxRates.Unregistered,
            TradeInvoiceLayout.UsesUnregisteredBillLevelTaxSplit(companyId),
            SalesTaxSplit.FurtherTaxRate(taxRates.Registered, taxRates.Unregistered));
    }

    public async Task<IReadOnlyList<SalesInvoiceItemLookupDto>> GetItemLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var defaultTaxRate = await GetDefaultTaxRateAsync(companyId, cancellationToken);

        var items = await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId && i.IsActive)
            .OrderBy(i => i.ItemName)
            .Select(i => new
            {
                i.Id,
                i.ItemCode,
                i.ItemName,
                i.Description,
                i.HSCode,
                i.StackNo,
                i.LotNo,
                i.ItemType,
                UnitSymbol = i.UnitOfMeasure.Symbol ?? "PCS",
                i.SaleRate
            })
            .ToListAsync(cancellationToken);

        return items
            .Select(i => new SalesInvoiceItemLookupDto(
                i.Id,
                i.ItemCode,
                i.ItemName,
                i.Description,
                i.HSCode,
                i.StackNo,
                i.LotNo,
                InventoryUnitDisplay.Format(i.ItemCode, i.UnitSymbol),
                i.SaleRate,
                defaultTaxRate,
                i.ItemType))
            .ToList();
    }

    public async Task<SalesInvoiceSaveResult> CreateAsync(
        SalesInvoiceSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        if (request.CustomerId <= 0)
        {
            return new SalesInvoiceSaveResult(false, "Customer is required.", null);
        }

        if (request.Lines.Count == 0)
        {
            return new SalesInvoiceSaveResult(false, "Add at least one invoice line.", null);
        }

        if (string.IsNullOrWhiteSpace(request.ShippingAddress))
        {
            return new SalesInvoiceSaveResult(false, "Shipping address is required.", null);
        }

        var customer = await _unitOfWork.Repository<Customer>()
            .Query()
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId && c.CompanyId == companyId, cancellationToken);

        if (customer is null)
        {
            return new SalesInvoiceSaveResult(false, "Customer not found.", null);
        }

        var invoiceNumber = string.IsNullOrWhiteSpace(request.InvoiceNumber)
            ? (await GenerateNextInvoiceNumberAsync(cancellationToken)).InvoiceNumber
            : request.InvoiceNumber.Trim();

        var numberExists = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .AnyAsync(i => i.CompanyId == companyId && i.InvoiceNumber == invoiceNumber, cancellationToken);

        if (numberExists)
        {
            return new SalesInvoiceSaveResult(false, "Invoice number already exists.", null);
        }

        var itemIds = request.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId && itemIds.Contains(i.Id))
            .Select(i => new
            {
                i.Id,
                i.ItemCode,
                i.HSCode,
                i.ItemName,
                i.Description,
                i.StackNo,
                i.LotNo,
                i.ItemType,
                UnitSymbol = i.UnitOfMeasure.Symbol ?? "PCS"
            })
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        if (items.Count != itemIds.Count)
        {
            return new SalesInvoiceSaveResult(false, "One or more items are invalid.", null);
        }

        var itemSnapshots = items.ToDictionary(
            x => x.Key,
            x => new InvoiceItemSnapshot(
                x.Value.Id,
                x.Value.ItemCode,
                x.Value.HSCode,
                x.Value.ItemName,
                x.Value.Description,
                x.Value.StackNo,
                x.Value.LotNo,
                x.Value.ItemType,
                x.Value.UnitSymbol));

        var taxContext = await BuildTaxContextAsync(
            companyId,
            request.ScenarioId ?? customer.ScenarioId,
            cancellationToken);
        var billTaxInput = BuildBillTaxInput(request, customer, taxContext);
        var lineBuild = BuildInvoiceLineEntities(request.Lines, itemSnapshots, taxContext, billTaxInput);
        if (!lineBuild.Success)
        {
            return new SalesInvoiceSaveResult(false, lineBuild.Message, null);
        }

        var stockValidation = await _stackLotInventory.ValidateSaleLinesAsync(
            request.InvoiceType,
            lineBuild.ValidationLines,
            excludeInvoiceId: null,
            cancellationToken);

        if (!stockValidation.Success)
        {
            return new SalesInvoiceSaveResult(false, stockValidation.Message, null);
        }

        var now = DateTime.UtcNow;
        var netTotal = Math.Round(lineBuild.SubTotal - lineBuild.DiscountTotal + lineBuild.TaxTotal, 2);

        var entity = new SalesInvoice
        {
            CompanyId = companyId,
            InvoiceNumber = invoiceNumber,
            CustomerId = customer.Id,
            BuyerAddress = request.BuyerAddress?.Trim() ?? customer.Address,
            ShippingAddress = request.ShippingAddress.Trim(),
            ProvinceId = request.ProvinceId ?? customer.ProvinceId,
            BuyerNTN = request.BuyerNTN?.Trim() ?? customer.NTN,
            BuyerCNIC = request.BuyerCNIC?.Trim() ?? customer.CNIC,
            InvoiceDate = request.InvoiceDate.Date,
            InvoiceType = request.InvoiceType,
            ScenarioId = request.ScenarioId ?? customer.ScenarioId,
            SubTotal = lineBuild.SubTotal,
            DiscountAmount = lineBuild.DiscountTotal,
            TaxAmount = lineBuild.SalesTaxTotal,
            FurtherTax = lineBuild.FurtherTaxTotal,
            NetTotal = netTotal,
            Status = InvoiceStatus.Draft,
            CreatedAt = now,
            CreatedBy = _currentUser.UserName ?? "system"
        };

        try
        {
            await _unitOfWork.Repository<SalesInvoice>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            foreach (var line in lineBuild.Lines)
            {
                line.SalesInvoiceId = entity.Id;
            }

            await _unitOfWork.Repository<SalesInvoiceLine>().AddRangeAsync(lineBuild.Lines, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to create sales invoice {InvoiceNumber}", invoiceNumber);
            return new SalesInvoiceSaveResult(false, "Could not save invoice. Check customer, items, and company.", null);
        }

        try
        {
            await _auditService.LogAsync("Create", "SalesInvoices", entity.Id.ToString(), null, invoiceNumber, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for sales invoice {InvoiceId}", entity.Id);
        }

        return new SalesInvoiceSaveResult(true, null, entity.Id);
    }

    public async Task<SalesInvoiceSaveResult> UpdateAsync(
        SalesInvoiceSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.Id.HasValue || request.Id.Value <= 0)
        {
            return new SalesInvoiceSaveResult(false, "Invoice id is required.", null);
        }

        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        if (request.CustomerId <= 0)
        {
            return new SalesInvoiceSaveResult(false, "Customer is required.", null);
        }

        if (request.Lines.Count == 0)
        {
            return new SalesInvoiceSaveResult(false, "Add at least one invoice line.", null);
        }

        if (string.IsNullOrWhiteSpace(request.ShippingAddress))
        {
            return new SalesInvoiceSaveResult(false, "Shipping address is required.", null);
        }

        var entity = await _unitOfWork.Repository<SalesInvoice>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(i => i.Id == request.Id.Value && i.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new SalesInvoiceSaveResult(false, "Invoice not found.", null);
        }

        if (entity.Status != InvoiceStatus.Draft)
        {
            return new SalesInvoiceSaveResult(false, "Only draft invoices can be edited.", null);
        }

        var customer = await _unitOfWork.Repository<Customer>()
            .Query()
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId && c.CompanyId == companyId, cancellationToken);

        if (customer is null)
        {
            return new SalesInvoiceSaveResult(false, "Customer not found.", null);
        }

        var invoiceNumber = string.IsNullOrWhiteSpace(request.InvoiceNumber)
            ? entity.InvoiceNumber
            : request.InvoiceNumber.Trim();

        var numberExists = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .AnyAsync(i => i.CompanyId == companyId
                           && i.InvoiceNumber == invoiceNumber
                           && i.Id != entity.Id, cancellationToken);

        if (numberExists)
        {
            return new SalesInvoiceSaveResult(false, "Invoice number already exists.", null);
        }

        var itemIds = request.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId && itemIds.Contains(i.Id))
            .Select(i => new
            {
                i.Id,
                i.ItemCode,
                i.HSCode,
                i.ItemName,
                i.Description,
                i.StackNo,
                i.LotNo,
                i.ItemType,
                UnitSymbol = i.UnitOfMeasure.Symbol ?? "PCS"
            })
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        if (items.Count != itemIds.Count)
        {
            return new SalesInvoiceSaveResult(false, "One or more items are invalid.", null);
        }

        var itemSnapshots = items.ToDictionary(
            x => x.Key,
            x => new InvoiceItemSnapshot(
                x.Value.Id,
                x.Value.ItemCode,
                x.Value.HSCode,
                x.Value.ItemName,
                x.Value.Description,
                x.Value.StackNo,
                x.Value.LotNo,
                x.Value.ItemType,
                x.Value.UnitSymbol));

        var taxContext = await BuildTaxContextAsync(
            companyId,
            request.ScenarioId ?? customer.ScenarioId,
            cancellationToken);
        var billTaxInput = BuildBillTaxInput(request, customer, taxContext);
        var lineBuild = BuildInvoiceLineEntities(request.Lines, itemSnapshots, taxContext, billTaxInput);
        if (!lineBuild.Success)
        {
            return new SalesInvoiceSaveResult(false, lineBuild.Message, null);
        }

        var stockValidation = await _stackLotInventory.ValidateSaleLinesAsync(
            request.InvoiceType,
            lineBuild.ValidationLines,
            excludeInvoiceId: entity.Id,
            cancellationToken);

        if (!stockValidation.Success)
        {
            return new SalesInvoiceSaveResult(false, stockValidation.Message, null);
        }

        var existingLines = await _unitOfWork.Repository<SalesInvoiceLine>()
            .Query(asNoTracking: false)
            .Where(l => l.SalesInvoiceId == entity.Id)
            .ToListAsync(cancellationToken);

        foreach (var existingLine in existingLines)
        {
            _unitOfWork.Repository<SalesInvoiceLine>().Remove(existingLine);
        }

        var now = DateTime.UtcNow;
        var netTotal = Math.Round(lineBuild.SubTotal - lineBuild.DiscountTotal + lineBuild.TaxTotal, 2);

        entity.InvoiceNumber = invoiceNumber;
        entity.CustomerId = customer.Id;
        entity.BuyerAddress = request.BuyerAddress?.Trim() ?? customer.Address;
        entity.ShippingAddress = request.ShippingAddress.Trim();
        entity.ProvinceId = request.ProvinceId ?? customer.ProvinceId;
        entity.BuyerNTN = request.BuyerNTN?.Trim() ?? customer.NTN;
        entity.BuyerCNIC = request.BuyerCNIC?.Trim() ?? customer.CNIC;
        entity.InvoiceDate = request.InvoiceDate.Date;
        entity.InvoiceType = request.InvoiceType;
        entity.ScenarioId = request.ScenarioId ?? customer.ScenarioId;
        entity.SubTotal = lineBuild.SubTotal;
        entity.DiscountAmount = lineBuild.DiscountTotal;
        entity.TaxAmount = lineBuild.SalesTaxTotal;
        entity.FurtherTax = lineBuild.FurtherTaxTotal;
        entity.NetTotal = netTotal;
        entity.UpdatedAt = now;
        entity.UpdatedBy = _currentUser.UserName ?? "system";

        try
        {
            _unitOfWork.Repository<SalesInvoice>().Update(entity);

            foreach (var line in lineBuild.Lines)
            {
                line.SalesInvoiceId = entity.Id;
            }

            await _unitOfWork.Repository<SalesInvoiceLine>().AddRangeAsync(lineBuild.Lines, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to update sales invoice {InvoiceId}", entity.Id);
            return new SalesInvoiceSaveResult(false, "Could not update invoice.", null);
        }

        await TryAuditAsync("Update", entity.Id.ToString(), null, invoiceNumber, cancellationToken);

        return new SalesInvoiceSaveResult(true, null, entity.Id);
    }

    private static InvoiceLineBuildResult BuildInvoiceLineEntities(
        IReadOnlyList<SalesInvoiceLineSaveRequest> lines,
        Dictionary<int, InvoiceItemSnapshot> items,
        InvoiceTaxContext taxContext,
        BillTaxInput billTaxInput)
    {
        var validationLines = new List<StackLotSaleValidationLine>();
        var lineEntities = new List<SalesInvoiceLine>();
        decimal subTotal = 0m;
        decimal discountTotal = 0m;
        decimal goodsTaxableTotal = 0m;
        decimal salesTaxTotal = 0m;
        decimal furtherTaxTotal = 0m;

        foreach (var line in lines)
        {
            if (line.ItemId <= 0 || line.Quantity <= 0 || line.Price < 0)
            {
                return InvoiceLineBuildResult.Failed("Each line needs an item, quantity, and price.");
            }

            var item = items[line.ItemId];
            var stackNo = string.IsNullOrWhiteSpace(line.StackNo) ? item.StackNo : line.StackNo.Trim();
            var lotNo = string.IsNullOrWhiteSpace(line.LotNo) ? item.LotNo : line.LotNo?.Trim();

            if (item.ItemType != ItemType.Service)
            {
                validationLines.Add(new StackLotSaleValidationLine(
                    line.ItemId,
                    item.ItemCode,
                    string.IsNullOrWhiteSpace(stackNo) ? null : stackNo,
                    string.IsNullOrWhiteSpace(lotNo) ? null : lotNo,
                    line.Quantity,
                    Math.Max(0m, line.Cartons)));
            }

            var lineSubTotal = Math.Round(line.Quantity * line.Price, 2);
            var lineDiscount = Math.Round(Math.Max(0m, line.Discount), 2);
            var taxable = Math.Round(lineSubTotal - lineDiscount, 2);
            decimal taxRate = 0m;
            decimal lineTax = 0m;
            var isCartageOrService = IsCartageOrService(item);

            if (!isCartageOrService)
            {
                taxRate = Math.Max(0m, line.TaxRate > 0m
                    ? line.TaxRate
                    : (taxContext.IsUnregisteredScenario
                        ? taxContext.UnregisteredRate
                        : taxContext.RegisteredRate));

                if (taxContext.UseBillLevelTaxSplit)
                {
                    goodsTaxableTotal += taxable;
                }
                else if (taxContext.UseSplitTaxGl && taxContext.IsUnregisteredScenario)
                {
                    var applyFurtherTax = SalesTaxSplit.ApplyFurtherTaxForLine(
                        true,
                        taxRate,
                        taxContext.RegisteredRate);
                    var (salesTax, furtherTax, lineTaxTotal) = SalesTaxSplit.CalculateLineTax(
                        taxable,
                        taxContext.RegisteredRate,
                        taxContext.UnregisteredRate,
                        applyFurtherTax,
                        billTaxInput.FurtherTaxRate,
                        taxRate);
                    lineTax = lineTaxTotal;
                    salesTaxTotal += salesTax;
                    furtherTaxTotal += furtherTax;
                }
                else
                {
                    lineTax = Math.Round(taxable * taxRate / 100m, 2);
                }
            }

            var lineTotal = taxContext.UseBillLevelTaxSplit && !isCartageOrService
                ? taxable
                : Math.Round(taxable + lineTax, 2);

            subTotal += lineSubTotal;
            discountTotal += lineDiscount;

            var productDescription = !string.IsNullOrWhiteSpace(line.ProductDescription)
                ? line.ProductDescription.Trim()
                : !string.IsNullOrWhiteSpace(item.Description)
                    ? item.Description.Trim()
                    : item.ItemName;

            lineEntities.Add(new SalesInvoiceLine
            {
                ItemId = line.ItemId,
                HSCode = item.HSCode,
                CartonDescription = string.IsNullOrWhiteSpace(line.CartonDescription)
                    ? null
                    : line.CartonDescription.Trim(),
                ProductDescription = productDescription,
                Unit = InventoryUnitDisplay.Format(item.ItemCode, item.UnitSymbol),
                StackNo = string.IsNullOrWhiteSpace(stackNo) ? null : stackNo,
                LotNo = string.IsNullOrWhiteSpace(lotNo) ? null : lotNo,
                Quantity = line.Quantity,
                Cartons = Math.Max(0m, line.Cartons),
                Price = line.Price,
                TaxRate = taxRate,
                TaxAmount = lineTax,
                Discount = lineDiscount,
                LineTotal = lineTotal
            });
        }

        decimal taxTotal;
        if (taxContext.UseBillLevelTaxSplit)
        {
            var defaultFurtherRate = SalesTaxSplit.FurtherTaxRate(
                taxContext.RegisteredRate,
                taxContext.UnregisteredRate);
            (salesTaxTotal, furtherTaxTotal, taxTotal) = SalesTaxSplit.CalculateBillTax(
                goodsTaxableTotal,
                taxContext.RegisteredRate,
                defaultFurtherRate,
                billTaxInput.FurtherTaxRate,
                billTaxInput.FurtherTaxAmount);
        }
        else if (taxContext.UseSplitTaxGl && taxContext.IsUnregisteredScenario)
        {
            salesTaxTotal = Math.Round(salesTaxTotal, 2);
            furtherTaxTotal = Math.Round(furtherTaxTotal, 2);
            taxTotal = salesTaxTotal + furtherTaxTotal;
        }
        else
        {
            taxTotal = lineEntities.Sum(l => l.TaxAmount);
            salesTaxTotal = taxTotal;
            furtherTaxTotal = 0m;
        }

        return InvoiceLineBuildResult.Succeeded(
            lineEntities,
            validationLines,
            subTotal,
            discountTotal,
            salesTaxTotal,
            furtherTaxTotal,
            taxTotal);
    }

    private sealed record BillTaxInput(decimal? FurtherTaxRate, decimal? FurtherTaxAmount);

    private static BillTaxInput BuildBillTaxInput(
        SalesInvoiceSaveRequest request,
        Customer customer,
        InvoiceTaxContext taxContext)
    {
        var defaultFurtherRate = SalesTaxSplit.FurtherTaxRate(
            taxContext.RegisteredRate,
            taxContext.UnregisteredRate);
        var effectiveFurtherRate = request.FurtherTaxRate
            ?? customer.FurtherTaxRate
            ?? defaultFurtherRate;
        return new BillTaxInput(effectiveFurtherRate, request.FurtherTaxAmount);
    }

    private sealed record InvoiceTaxContext(
        bool IsUnregisteredScenario,
        bool UseBillLevelTaxSplit,
        bool UseSplitTaxGl,
        decimal RegisteredRate,
        decimal UnregisteredRate);

    private async Task<InvoiceTaxContext> BuildTaxContextAsync(
        int companyId,
        int? scenarioId,
        CancellationToken cancellationToken)
    {
        var rates = await GetCompanyTaxRatesAsync(companyId, cancellationToken);
        if (!scenarioId.HasValue)
        {
            return new InvoiceTaxContext(false, false, false, rates.Registered, rates.Unregistered);
        }

        var scenarioCode = await _unitOfWork.Repository<ScenarioType>()
            .Query()
            .Where(s => s.ScenarioId == scenarioId.Value)
            .Select(s => s.Code)
            .FirstOrDefaultAsync(cancellationToken);

        var isUnregistered = SalesTaxSplit.IsUnregisteredScenario(scenarioCode);
        var useSplitTaxGl = TradeInvoiceLayout.UsesSplitTaxSubAccounts(companyId);
        return new InvoiceTaxContext(
            isUnregistered,
            isUnregistered && TradeInvoiceLayout.UsesUnregisteredBillLevelTaxSplit(companyId),
            isUnregistered && useSplitTaxGl,
            rates.Registered,
            rates.Unregistered);
    }

    private sealed record InvoiceItemSnapshot(
        int Id,
        string ItemCode,
        string? HSCode,
        string ItemName,
        string? Description,
        string StackNo,
        string LotNo,
        ItemType ItemType,
        string UnitSymbol);

    private sealed record InvoiceLineBuildResult(
        bool Success,
        string? Message,
        List<SalesInvoiceLine> Lines,
        List<StackLotSaleValidationLine> ValidationLines,
        decimal SubTotal,
        decimal DiscountTotal,
        decimal SalesTaxTotal,
        decimal FurtherTaxTotal,
        decimal TaxTotal)
    {
        public static InvoiceLineBuildResult Failed(string message) =>
            new(false, message, [], [], 0m, 0m, 0m, 0m, 0m);

        public static InvoiceLineBuildResult Succeeded(
            List<SalesInvoiceLine> lines,
            List<StackLotSaleValidationLine> validationLines,
            decimal subTotal,
            decimal discountTotal,
            decimal salesTaxTotal,
            decimal furtherTaxTotal,
            decimal taxTotal) =>
            new(true, null, lines, validationLines, subTotal, discountTotal, salesTaxTotal, furtherTaxTotal, taxTotal);
    }

    public async Task<SalesInvoiceDetailDto?> GetDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var invoice = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(i => i.Id == id && i.CompanyId == companyId)
            .Select(i => new
            {
                i.Id,
                i.InvoiceNumber,
                i.CustomerId,
                CustomerName = i.Customer.BuyerName,
                CustomerCode = i.Customer.BuyerId,
                i.InvoiceDate,
                i.InvoiceType,
                i.ScenarioId,
                ScenarioCode = i.ScenarioType != null ? i.ScenarioType.Code : null,
                i.BuyerAddress,
                i.ShippingAddress,
                BuyerProvince = i.Province != null
                    ? i.Province.Name
                    : i.Customer.Province != null
                        ? i.Customer.Province.Name
                        : null,
                i.BuyerNTN,
                i.BuyerCNIC,
                SellerCompanyName = i.Company.CompanyName,
                SellerNtn = i.Company.NTN,
                SellerAddress = i.Company.Address,
                SellerProvince = i.Company.Province != null ? i.Company.Province.Name : null,
                SellerPhone = i.Company.Phone,
                SellerEmail = i.Company.Email,
                i.SubTotal,
                i.DiscountAmount,
                i.TaxAmount,
                i.FurtherTax,
                i.NetTotal,
                i.Status,
                i.FbrInvoiceNumber,
                i.FbrSubmittedAt,
                i.JournalEntryId,
                JournalEntryNumber = i.JournalEntry != null ? i.JournalEntry.EntryNumber : null,
                Lines = i.Lines.Select(l => new SalesInvoiceLineDto(
                    l.Id,
                    l.ItemId,
                    l.Item.ItemCode,
                    l.Item.ItemName,
                    l.Item.Description,
                    l.HSCode,
                    l.CartonDescription,
                    l.ProductDescription,
                    l.Unit,
                    l.StackNo,
                    l.LotNo,
                    l.Quantity,
                    l.Cartons,
                    l.Price,
                    l.TaxRate,
                    l.TaxAmount,
                    l.Discount,
                    l.LineTotal)).ToList(),
                Attachments = i.Attachments.Select(a => new SalesInvoiceAttachmentDto(
                    a.Id,
                    a.FileName,
                    a.ContentType,
                    a.FileSizeBytes,
                    a.CreatedAt,
                    a.CreatedBy)).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (invoice is null)
        {
            return null;
        }

        var customerBalance = await GetCustomerBalanceAsOfAsync(
            invoice.CustomerId,
            invoice.InvoiceDate,
            cancellationToken);
        var hasFbrPdf = invoice.FbrSubmittedAt != null;
        var canDownloadInvoicePdf = hasFbrPdf
            || (companyId == TradeInvoiceLayout.TradeInvoiceCompanyId
                && invoice.Status == InvoiceStatus.Posted);

        return new SalesInvoiceDetailDto(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.CustomerId,
            invoice.CustomerName,
            invoice.CustomerCode,
            invoice.InvoiceDate,
            invoice.InvoiceType,
            invoice.ScenarioId,
            invoice.ScenarioCode,
            invoice.BuyerAddress,
            invoice.ShippingAddress,
            invoice.BuyerProvince,
            invoice.BuyerNTN,
            invoice.BuyerCNIC,
            invoice.SellerCompanyName,
            invoice.SellerNtn,
            invoice.SellerAddress,
            invoice.SellerProvince,
            invoice.SellerPhone,
            invoice.SellerEmail,
            invoice.SubTotal,
            invoice.DiscountAmount,
            invoice.TaxAmount,
            invoice.FurtherTax,
            invoice.NetTotal,
            invoice.Status,
            invoice.FbrInvoiceNumber,
            invoice.FbrSubmittedAt,
            invoice.JournalEntryId,
            invoice.JournalEntryNumber,
            hasFbrPdf,
            invoice.Lines,
            invoice.Attachments,
            companyId,
            customerBalance,
            canDownloadInvoicePdf);
    }

    public async Task<SalesInvoiceActionResult> PostAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return ToActionError(companyError!);
        }

        var invoice = await _unitOfWork.Repository<SalesInvoice>()
            .Query(asNoTracking: false)
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id && i.CompanyId == companyId, cancellationToken);

        if (invoice is null)
        {
            return new SalesInvoiceActionResult(false, "Invoice not found.", null);
        }

        if (invoice.Status != InvoiceStatus.Draft)
        {
            return new SalesInvoiceActionResult(false, "Only draft invoices can be posted.", null);
        }

        if (invoice.Lines.Count == 0)
        {
            return new SalesInvoiceActionResult(false, "Invoice has no line items.", null);
        }

        var accounts = await ResolvePostingAccountsAsync(companyId, invoice.InvoiceType, cancellationToken);
        if (!accounts.Success)
        {
            return new SalesInvoiceActionResult(false, accounts.Message, null);
        }

        var lineItemIds = invoice.Lines.Select(l => l.ItemId).Distinct().ToList();
        var cartageItemIds = await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => lineItemIds.Contains(i.Id)
                && i.CompanyId == companyId
                && i.ItemCode == CartageItemCode)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);
        var cartageItemIdSet = cartageItemIds.ToHashSet();

        decimal goodsSalesAmount = 0m;
        decimal lineTaxTotal = 0m;
        decimal cartageAmount = 0m;

        foreach (var line in invoice.Lines)
        {
            if (cartageItemIdSet.Contains(line.ItemId))
            {
                cartageAmount += Math.Round(line.LineTotal, 2);
                continue;
            }

            goodsSalesAmount += Math.Round(Math.Max(0m, line.Quantity * line.Price - line.Discount), 2);
            lineTaxTotal += Math.Round(line.TaxAmount, 2);
        }

        goodsSalesAmount = Math.Round(goodsSalesAmount, 2);
        lineTaxTotal = Math.Round(lineTaxTotal, 2);
        var taxRates = await GetCompanyTaxRatesAsync(companyId, cancellationToken);
        var scenarioCode = invoice.ScenarioId.HasValue
            ? await _unitOfWork.Repository<ScenarioType>()
                .Query()
                .Where(s => s.ScenarioId == invoice.ScenarioId.Value)
                .Select(s => s.Code)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var isUnregisteredScenario = SalesTaxSplit.IsUnregisteredScenario(scenarioCode);
        var useSplitTaxGl = TradeInvoiceLayout.UsesSplitTaxSubAccounts(companyId) && isUnregisteredScenario;

        decimal salesTaxAmount;
        decimal furtherTaxAmount;
        if (useSplitTaxGl && goodsSalesAmount > 0m)
        {
            salesTaxAmount = Math.Round(invoice.TaxAmount, 2);
            furtherTaxAmount = Math.Round(invoice.FurtherTax, 2);
        }
        else
        {
            furtherTaxAmount = Math.Round(invoice.FurtherTax, 2);
            salesTaxAmount = furtherTaxAmount > 0m
                ? Math.Round(invoice.TaxAmount, 2)
                : lineTaxTotal;
        }

        cartageAmount = Math.Round(cartageAmount, 2);
        var netTotal = Math.Round(invoice.NetTotal, 2);

        if (goodsSalesAmount + salesTaxAmount + furtherTaxAmount + cartageAmount != netTotal)
        {
            return new SalesInvoiceActionResult(false, "Invoice totals are inconsistent. Cannot post.", null);
        }

        if (cartageAmount > 0m && accounts.CartageAccountId is null)
        {
            return new SalesInvoiceActionResult(
                false,
                $"Chart of account {CartagePayable} (Cartage Payable) not found.",
                null);
        }

        if (useSplitTaxGl && furtherTaxAmount > 0m && accounts.FurtherTaxAccountId is null)
        {
            return new SalesInvoiceActionResult(
                false,
                $"Chart of account {FurtherTaxPayable} (Further Tax Payable) not found.",
                null);
        }

        if (useSplitTaxGl && salesTaxAmount > 0m && accounts.SalesTax18AccountId is null)
        {
            return new SalesInvoiceActionResult(
                false,
                $"Chart of account {SalesTaxPayable18} (Sales Tax Payable 18%) not found.",
                null);
        }

        var inventoryItemIds = invoice.Lines
            .Where(l => !cartageItemIdSet.Contains(l.ItemId))
            .Select(l => l.ItemId)
            .Distinct()
            .ToList();

        var inventoryItems = inventoryItemIds.Count > 0
            ? await _unitOfWork.Repository<Item>()
                .Query(asNoTracking: false)
                .Where(i => i.CompanyId == companyId && inventoryItemIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, cancellationToken)
            : new Dictionary<int, Item>();

        var costingBatch = await _inventoryCosting.CreateBatchAsync(companyId, inventoryItemIds, cancellationToken);
        var lineCosts = new Dictionary<int, InventoryLineCostResult>();

        decimal cogsAmount = 0m;
        foreach (var line in invoice.Lines)
        {
            if (cartageItemIdSet.Contains(line.ItemId))
            {
                continue;
            }

            if (!inventoryItems.TryGetValue(line.ItemId, out var item)
                || item.ItemType == ItemType.Service)
            {
                continue;
            }

            var quantity = Math.Round(line.Quantity, 2);
            if (quantity <= 0m)
            {
                continue;
            }

            var costResult = costingBatch.Calculate(new InventoryLineCostRequest(
                item.Id,
                line.StackNo,
                line.LotNo,
                item.StackNo,
                item.LotNo,
                quantity,
                item.CostingMethod,
                item.PurchaseRate));

            if (!costResult.Success)
            {
                return new SalesInvoiceActionResult(false, costResult.Message, null);
            }

            lineCosts[line.Id] = costResult;
            cogsAmount += costResult.TotalCost;
        }

        cogsAmount = Math.Round(cogsAmount, 2);

        if (cogsAmount > 0m)
        {
            if (accounts.CogsAccountId is null)
            {
                return new SalesInvoiceActionResult(
                    false,
                    $"Chart of account {CostOfGoodsSold} (Cost of Goods Sold) not found.",
                    null);
            }

            if (accounts.InventoryAccountId is null)
            {
                return new SalesInvoiceActionResult(
                    false,
                    $"Chart of account {InventoryAsset} (Inventory Asset) not found.",
                    null);
            }
        }

        var journalLines = BuildJournalLines(
            invoice.InvoiceType,
            accounts.ArAccountId,
            accounts.RevenueAccountId,
            accounts.TaxAccountId,
            accounts.SalesTax18AccountId,
            accounts.FurtherTaxAccountId,
            useSplitTaxGl && salesTaxAmount + furtherTaxAmount > 0m,
            accounts.CartageAccountId,
            accounts.CogsAccountId,
            accounts.InventoryAccountId,
            goodsSalesAmount,
            salesTaxAmount,
            furtherTaxAmount,
            cartageAmount,
            cogsAmount,
            netTotal);

        var entryNumber = await GenerateNextJournalEntryNumberAsync(companyId, cancellationToken);
        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "system";

        var journalEntry = new JournalEntry
        {
            CompanyId = companyId,
            EntryNumber = entryNumber,
            EntryDate = invoice.InvoiceDate,
            Description = $"Sales invoice {invoice.InvoiceNumber}",
            ReferenceType = ReferenceTypes.SalesInvoice,
            ReferenceId = invoice.Id,
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

            if (inventoryItems.Count > 0)
            {
                var goodsLines = invoice.Lines
                    .Where(l => !cartageItemIdSet.Contains(l.ItemId)
                                && inventoryItems.TryGetValue(l.ItemId, out var item)
                                && item.ItemType != ItemType.Service)
                    .ToList();

                if (goodsLines.Count > 0
                    && invoice.InvoiceType is InvoiceType.SalesInvoice or InvoiceType.CreditNote)
                {
                    var warehouseId = await GetDefaultWarehouseIdAsync(companyId, cancellationToken);
                    if (!warehouseId.HasValue)
                    {
                        return new SalesInvoiceActionResult(
                            false,
                            "No active warehouse found. Add a warehouse before posting inventory invoices.",
                            null);
                    }

                    var isCreditNote = invoice.InvoiceType == InvoiceType.CreditNote;
                    var inventoryTransactions = new List<InventoryTransaction>();

                    foreach (var line in goodsLines)
                    {
                        var item = inventoryItems[line.ItemId];
                        var quantity = Math.Round(line.Quantity, 2);
                        if (quantity <= 0m)
                        {
                            continue;
                        }

                        if (!isCreditNote && item.CurrentStock < quantity)
                        {
                            return new SalesInvoiceActionResult(
                                false,
                                $"Insufficient stock for {item.ItemCode}. Available: {item.CurrentStock:N2}",
                                null);
                        }

                        var unitCost = lineCosts.TryGetValue(line.Id, out var lineCost)
                            ? lineCost.UnitCost
                            : Math.Round(item.PurchaseRate, 2);
                        var lineTotalCost = lineCosts.TryGetValue(line.Id, out var lineCostTotal)
                            ? lineCostTotal.TotalCost
                            : Math.Round(quantity * unitCost, 2);
                        var transactionType = isCreditNote
                            ? InventoryTransactionType.StockIn
                            : InventoryTransactionType.StockOut;

                        inventoryTransactions.Add(new InventoryTransaction
                        {
                            CompanyId = companyId,
                            ItemId = item.Id,
                            WarehouseId = warehouseId.Value,
                            TransactionType = transactionType,
                            StackNo = string.IsNullOrWhiteSpace(line.StackNo) ? null : line.StackNo.Trim(),
                            LotNo = string.IsNullOrWhiteSpace(line.LotNo) ? null : line.LotNo.Trim(),
                            Quantity = quantity,
                            UnitCost = unitCost,
                            TotalCost = lineTotalCost,
                            TransactionDate = invoice.InvoiceDate,
                            ReferenceNo = invoice.InvoiceNumber,
                            Notes = $"Sales invoice {invoice.InvoiceNumber}",
                            CreatedAt = now,
                            CreatedBy = userName
                        });

                        item.CurrentStock = isCreditNote
                            ? Math.Round(item.CurrentStock + quantity, 2)
                            : Math.Round(item.CurrentStock - quantity, 2);
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

            invoice.Status = InvoiceStatus.Posted;
            invoice.JournalEntryId = journalEntry.Id;
            invoice.UpdatedAt = now;
            invoice.UpdatedBy = userName;

            _unitOfWork.Repository<SalesInvoice>().Update(invoice);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (inventoryItemIds.Count > 0)
            {
                await _itemCartonSyncService.SyncItemsAsync(companyId, inventoryItemIds, cancellationToken);
            }
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to post sales invoice {InvoiceId}", id);
            return new SalesInvoiceActionResult(false, "Could not post invoice to the general ledger.", null);
        }

        await TryAuditAsync("Post", id.ToString(), InvoiceStatus.Draft.ToString(), InvoiceStatus.Posted.ToString(), cancellationToken);

        var detail = await GetDetailAsync(id, cancellationToken);
        return new SalesInvoiceActionResult(true, "Invoice posted to the general ledger.", detail);
    }

    public async Task<SalesInvoiceActionResult> CancelAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return ToActionError(companyError!);
        }

        var invoice = await _unitOfWork.Repository<SalesInvoice>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(i => i.Id == id && i.CompanyId == companyId, cancellationToken);

        if (invoice is null)
        {
            return new SalesInvoiceActionResult(false, "Invoice not found.", null);
        }

        if (invoice.Status != InvoiceStatus.Draft)
        {
            return new SalesInvoiceActionResult(false, "Only draft invoices can be cancelled.", null);
        }

        invoice.Status = InvoiceStatus.Cancelled;
        invoice.UpdatedAt = DateTime.UtcNow;
        invoice.UpdatedBy = _currentUser.UserName;

        _unitOfWork.Repository<SalesInvoice>().Update(invoice);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await TryAuditAsync("Cancel", id.ToString(), InvoiceStatus.Draft.ToString(), InvoiceStatus.Cancelled.ToString(), cancellationToken);

        var detail = await GetDetailAsync(id, cancellationToken);
        return new SalesInvoiceActionResult(true, "Invoice cancelled.", detail);
    }

    public async Task<SalesInvoiceActionResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return ToActionError(companyError!);
        }

        var invoice = await _unitOfWork.Repository<SalesInvoice>()
            .Query(asNoTracking: false)
            .Include(i => i.Lines)
            .Include(i => i.Attachments)
            .FirstOrDefaultAsync(i => i.Id == id && i.CompanyId == companyId, cancellationToken);

        if (invoice is null)
        {
            return new SalesInvoiceActionResult(false, "Invoice not found.", null);
        }

        if (invoice.FbrSubmittedAt.HasValue)
        {
            return new SalesInvoiceActionResult(
                false,
                "Invoices submitted to FBR cannot be deleted.",
                null);
        }

        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName ?? "system";
        var invoiceNumber = invoice.InvoiceNumber;
        var affectedItemIds = new List<int>();

        if (invoice.Status == InvoiceStatus.Posted
            && invoice.InvoiceType is InvoiceType.SalesInvoice or InvoiceType.CreditNote)
        {
            affectedItemIds = await ReversePostedInvoiceInventoryAsync(
                companyId,
                invoice,
                cancellationToken);
        }

        await SoftDeleteSalesInvoiceJournalEntriesAsync(
            companyId,
            invoice.Id,
            invoice.JournalEntryId,
            now,
            userName,
            cancellationToken);

        foreach (var line in invoice.Lines.ToList())
        {
            _unitOfWork.Repository<SalesInvoiceLine>().Remove(line);
        }

        foreach (var attachment in invoice.Attachments.ToList())
        {
            _unitOfWork.Repository<SalesInvoiceAttachment>().Remove(attachment);
        }

        invoice.JournalEntryId = null;
        _unitOfWork.Repository<SalesInvoice>().Update(invoice);
        _unitOfWork.Repository<SalesInvoice>().Remove(invoice);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (affectedItemIds.Count > 0)
            {
                await RecalculateItemStockFromTransactionsAsync(
                    companyId,
                    affectedItemIds,
                    now,
                    userName,
                    cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _itemCartonSyncService.SyncItemsAsync(companyId, affectedItemIds, cancellationToken);
            }
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to delete sales invoice {InvoiceId}", id);
            return new SalesInvoiceActionResult(false, "Could not delete invoice.", null);
        }

        await TryAuditAsync("Delete", id.ToString(), invoiceNumber, null, cancellationToken);

        return new SalesInvoiceActionResult(true, "Invoice deleted.", null);
    }

    public async Task<SalesInvoiceActionResult> SubmitToFbrAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return ToActionError(companyError!);
        }

        var invoice = await _unitOfWork.Repository<SalesInvoice>()
            .Query(asNoTracking: false)
            .Include(i => i.Lines)
            .Include(i => i.Customer)
            .Include(i => i.ScenarioType)
            .FirstOrDefaultAsync(i => i.Id == id && i.CompanyId == companyId, cancellationToken);

        if (invoice is null)
        {
            return new SalesInvoiceActionResult(false, "Invoice not found.", null);
        }

        if (invoice.Status != InvoiceStatus.Posted)
        {
            return new SalesInvoiceActionResult(false, "Only posted invoices can be submitted to FBR.", null);
        }

        if (invoice.FbrSubmittedAt.HasValue)
        {
            return new SalesInvoiceActionResult(false, "This invoice has already been submitted to FBR.", null);
        }

        if (!invoice.ScenarioId.HasValue || invoice.ScenarioType is null)
        {
            return new SalesInvoiceActionResult(false, "FBR scenario is required before submission.", null);
        }

        var buildResult = await BuildFbrSubmissionRequestAsync(invoice, companyId, cancellationToken);
        if (!buildResult.Success || buildResult.Request is null)
        {
            return new SalesInvoiceActionResult(false, buildResult.Message, null);
        }

        var fbrResult = await _fbrSubmissionService.SubmitAsync(
            buildResult.Request,
            buildResult.FbrPostUrl,
            buildResult.ApiToken,
            cancellationToken);

        if (!fbrResult.Success)
        {
            return new SalesInvoiceActionResult(false, fbrResult.Message, null);
        }

        invoice.FbrInvoiceNumber = fbrResult.FbrInvoiceNumber;
        invoice.FbrResponseJson = fbrResult.ResponseJson;
        invoice.FbrSubmittedAt = DateTime.UtcNow;
        invoice.UpdatedAt = DateTime.UtcNow;
        invoice.UpdatedBy = _currentUser.UserName;

        _unitOfWork.Repository<SalesInvoice>().Update(invoice);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await TryAuditAsync(
            "FbrSubmit",
            id.ToString(),
            null,
            fbrResult.FbrInvoiceNumber,
            cancellationToken);

        var detail = await GetDetailAsync(id, cancellationToken);
        var message = fbrResult.IsSimulation
            ? "FBR submission stored in simulation mode."
            : "Invoice submitted to FBR successfully.";

        return new SalesInvoiceActionResult(true, message, detail);
    }

    public async Task<FbrPayloadPreviewDto?> GetFbrPayloadPreviewAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out _))
        {
            return null;
        }

        var invoice = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Include(i => i.Lines)
            .Include(i => i.Customer)
            .Include(i => i.ScenarioType)
            .FirstOrDefaultAsync(i => i.Id == id && i.CompanyId == companyId, cancellationToken);

        if (invoice is null)
        {
            return null;
        }

        if (invoice.Status != InvoiceStatus.Posted)
        {
            return null;
        }

        if (invoice.FbrSubmittedAt.HasValue)
        {
            return null;
        }

        if (!invoice.ScenarioId.HasValue || invoice.ScenarioType is null)
        {
            return null;
        }

        var buildResult = await BuildFbrSubmissionRequestAsync(invoice, companyId, cancellationToken);
        if (!buildResult.Success || buildResult.Request is null)
        {
            return null;
        }

        var isSimulation = string.IsNullOrWhiteSpace(buildResult.FbrPostUrl)
                           || string.IsNullOrWhiteSpace(buildResult.ApiToken);

        return new FbrPayloadPreviewDto(
            invoice.Id,
            invoice.InvoiceNumber,
            FbrPayloadBuilder.BuildJson(buildResult.Request),
            isSimulation,
            FbrPayloadBuilder.SystemGeneratedFooter);
    }

    public async Task<SalesInvoicePrintDto?> GetPrintDataAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out _))
        {
            return null;
        }

        var invoice = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(i => i.Id == id && i.CompanyId == companyId && i.FbrSubmittedAt != null)
            .Select(i => new
            {
                i.InvoiceNumber,
                i.FbrInvoiceNumber,
                i.InvoiceDate,
                i.InvoiceType,
                ScenarioCode = i.ScenarioType != null ? i.ScenarioType.Code : null,
                SellerName = i.Company.CompanyName,
                SellerNtn = i.Company.NTN,
                SellerAddress = i.Company.Address,
                SellerProvince = i.Company.Province != null ? i.Company.Province.Name : null,
                SellerPhone = i.Company.Phone,
                SellerEmail = i.Company.Email,
                BuyerName = i.Customer.BuyerName,
                BuyerId = i.Customer.BuyerId,
                BuyerNtn = i.BuyerNTN ?? i.Customer.NTN,
                BuyerCnic = i.BuyerCNIC ?? i.Customer.CNIC,
                BuyerAddress = i.BuyerAddress ?? i.Customer.Address,
                BuyerProvince = i.Province != null ? i.Province.Name : null,
                i.SubTotal,
                i.DiscountAmount,
                i.TaxAmount,
                i.NetTotal,
                Lines = i.Lines.Select(l => new
                {
                    l.ProductDescription,
                    ItemDescription = l.Item.Description,
                    l.HSCode,
                    l.StackNo,
                    l.LotNo,
                    l.Unit,
                    l.Quantity,
                    l.Price,
                    l.TaxRate,
                    l.Discount,
                    l.TaxAmount,
                    l.LineTotal
                }).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (invoice is null)
        {
            return null;
        }

        var taxRates = await GetCompanyTaxRatesAsync(companyId, cancellationToken);
        var isUnregistered = SalesTaxSplit.IsUnregisteredScenario(invoice.ScenarioCode);
        var useBillLevelTaxSplit = isUnregistered
            && TradeInvoiceLayout.UsesUnregisteredBillLevelTaxSplit(companyId);
        var saleType = FbrPayloadBuilder.MapSaleType(invoice.ScenarioCode);
        var exclusiveTotal = Math.Round(invoice.SubTotal - invoice.DiscountAmount, 2);
        var lines = invoice.Lines.Select((l, index) =>
        {
            var valueExcludingSt = Math.Round(Math.Max(0m, l.Quantity * l.Price - l.Discount), 2);
            decimal salesTax;
            decimal furtherTax;
            decimal displayRate;
            if (useBillLevelTaxSplit && l.TaxRate > 0m)
            {
                salesTax = 0m;
                furtherTax = 0m;
                displayRate = taxRates.Registered;
            }
            else
            {
                var applyFurtherTax = SalesTaxSplit.ApplyFurtherTaxForLine(
                    isUnregistered && l.TaxRate > 0m,
                    l.TaxRate,
                    taxRates.Registered);
                (salesTax, furtherTax, _) = SalesTaxSplit.CalculateLineTax(
                    valueExcludingSt,
                    taxRates.Registered,
                    taxRates.Unregistered,
                    applyFurtherTax,
                    null,
                    l.TaxRate);
                displayRate = applyFurtherTax ? taxRates.Registered : l.TaxRate;
            }

            return new SalesInvoicePrintLineDto(
                index + 1,
                FbrInvoiceLayout.BuildFbrProductDescription(
                    l.ProductDescription ?? l.ItemDescription,
                    l.LotNo,
                    l.StackNo),
                l.HSCode,
                saleType,
                Math.Round(l.Quantity, 2),
                l.Unit,
                FbrInvoiceLayout.FormatTaxRate(displayRate),
                valueExcludingSt,
                salesTax,
                furtherTax,
                Math.Round(l.LineTotal, 2));
        }).ToList();

        return new SalesInvoicePrintDto(
            invoice.InvoiceNumber,
            invoice.FbrInvoiceNumber,
            invoice.InvoiceDate,
            invoice.InvoiceType,
            invoice.ScenarioCode,
            FbrInvoiceLayout.FormatTaxPeriod(invoice.InvoiceDate),
            FbrInvoiceLayout.MapInvoiceTypeLabel(invoice.InvoiceType),
            new SalesInvoicePrintPartyDto(
                invoice.SellerName,
                invoice.SellerNtn,
                null,
                invoice.SellerAddress,
                invoice.SellerProvince,
                invoice.SellerPhone,
                invoice.SellerEmail),
            new SalesInvoicePrintPartyDto(
                invoice.BuyerName,
                invoice.BuyerNtn,
                invoice.BuyerCnic,
                invoice.BuyerAddress,
                invoice.BuyerProvince,
                null,
                null,
                invoice.BuyerId),
            exclusiveTotal,
            Math.Round(invoice.TaxAmount, 2),
            Math.Round(invoice.NetTotal, 2),
            AmountInWords.ToPakistaniRupees(invoice.NetTotal),
            DateTime.Now,
            lines,
            FbrInvoiceLayout.PdfFooterNotice);
    }

    public async Task<DeliveryChallanPrintDto?> GetDeliveryChallanDataAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out _))
        {
            return null;
        }

        var invoice = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(i => i.Id == id && i.CompanyId == companyId)
            .Select(i => new
            {
                i.InvoiceNumber,
                i.InvoiceDate,
                SellerName = i.Company.CompanyName,
                SellerAddress = i.Company.Address,
                SellerPhone = i.Company.Phone,
                BuyerName = i.Customer.BuyerName,
                BuyerNtn = i.BuyerNTN ?? i.Customer.NTN,
                BuyerCnic = i.BuyerCNIC ?? i.Customer.CNIC,
                BuyerAddress = i.ShippingAddress,
                BuyerProvince = i.Province != null
                    ? i.Province.Name
                    : (i.Customer.Province != null ? i.Customer.Province.Name : null),
                Lines = i.Lines.Select(l => new
                {
                    l.ProductDescription,
                    ItemDescription = l.Item.Description,
                    ItemCode = l.Item.ItemCode,
                    l.LineTotal,
                    l.CartonDescription,
                    l.LotNo,
                    l.StackNo,
                    l.Cartons,
                    l.Quantity,
                    l.Unit
                }).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (invoice is null)
        {
            return null;
        }

        var goodsLines = invoice.Lines
            .Where(l => !string.Equals(l.ItemCode, CartageItemCode, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var transportationChargesReceive = Math.Round(
            invoice.Lines
                .Where(l => string.Equals(l.ItemCode, CartageItemCode, StringComparison.OrdinalIgnoreCase))
                .Sum(l => l.LineTotal),
            2);

        var lines = goodsLines.Select((l, index) =>
        {
            var itemDescription = !string.IsNullOrWhiteSpace(l.ProductDescription)
                ? l.ProductDescription.Trim()
                : (l.ItemDescription ?? "—");

            return new DeliveryChallanPrintLineDto(
                index + 1,
                itemDescription,
                l.LotNo,
                l.StackNo,
                Math.Round(l.Cartons, 2),
                Math.Round(l.Quantity, 2),
                l.Unit,
                l.CartonDescription);
        }).ToList();

        if (transportationChargesReceive > 0m)
        {
            var transportDescription = invoice.Lines
                .Where(l => string.Equals(l.ItemCode, CartageItemCode, StringComparison.OrdinalIgnoreCase))
                .Select(l => !string.IsNullOrWhiteSpace(l.ProductDescription)
                    ? l.ProductDescription.Trim()
                    : l.ItemDescription)
                .FirstOrDefault(description => !string.IsNullOrWhiteSpace(description))
                ?? "Transportation Charges Receive";

            lines.Add(new DeliveryChallanPrintLineDto(
                lines.Count + 1,
                transportDescription,
                null,
                null,
                0m,
                0m,
                null,
                null,
                transportationChargesReceive,
                true));
        }

        return new DeliveryChallanPrintDto(
            invoice.InvoiceNumber,
            invoice.InvoiceDate,
            invoice.SellerName,
            invoice.SellerAddress,
            invoice.SellerPhone,
            invoice.BuyerName,
            invoice.BuyerAddress,
            invoice.BuyerProvince,
            invoice.BuyerNtn,
            invoice.BuyerCnic,
            DateTime.Now,
            lines,
            transportationChargesReceive,
            companyId);
    }

    public async Task<TradeInvoicePrintDto?> GetTradeInvoicePrintDataAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out _))
        {
            return null;
        }

        if (companyId != TradeInvoiceLayout.TradeInvoiceCompanyId)
        {
            return null;
        }

        var invoice = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(i => i.Id == id
                        && i.CompanyId == companyId
                        && i.Status == InvoiceStatus.Posted)
            .Select(i => new
            {
                i.InvoiceNumber,
                i.InvoiceDate,
                i.CustomerId,
                SellerName = i.Company.CompanyName,
                CustomerName = i.Customer.BuyerName,
                i.SubTotal,
                i.DiscountAmount,
                i.TaxAmount,
                i.NetTotal,
                Lines = i.Lines.Select(l => new
                {
                    l.ProductDescription,
                    ItemDescription = l.Item.Description,
                    l.CartonDescription,
                    l.LotNo,
                    l.StackNo,
                    l.Cartons,
                    l.Quantity,
                    l.Price,
                    l.Discount,
                    l.TaxRate
                }).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (invoice is null)
        {
            return null;
        }

        var lines = invoice.Lines.Select(l =>
        {
            var amount = TradeInvoiceLayout.LineAmountExTax(l.Quantity, l.Price, l.Discount);
            return new TradeInvoicePrintLineDto(
                TradeInvoiceLayout.BuildDescription(
                    l.ProductDescription,
                    l.ItemDescription,
                    l.LotNo,
                    l.StackNo),
                l.CartonDescription,
                Math.Round(l.Cartons, 2),
                Math.Round(l.Quantity, 2),
                Math.Round(l.Price, 2),
                amount);
        }).ToList();

        var taxableTotal = Math.Round(invoice.SubTotal - invoice.DiscountAmount, 2);
        var taxRateDisplay = TradeInvoiceLayout.ResolveTaxRateDisplay(
            taxableTotal,
            invoice.TaxAmount,
            invoice.Lines.Select(l => l.TaxRate).ToList());
        var customerBalance = await GetCustomerBalanceAsOfAsync(
            invoice.CustomerId,
            invoice.InvoiceDate,
            cancellationToken);

        return new TradeInvoicePrintDto(
            invoice.InvoiceNumber,
            invoice.InvoiceDate,
            invoice.SellerName,
            invoice.CustomerName,
            Math.Round(customerBalance, 2),
            taxableTotal,
            Math.Round(invoice.TaxAmount, 2),
            taxRateDisplay,
            Math.Round(invoice.NetTotal, 2),
            DateTime.Now,
            lines);
    }

    public async Task<IReadOnlyList<SubmittedInvoicePrintListItemDto>> GetSubmittedInvoicesForPrintAsync(
        string? buyerName,
        string? invoiceNumber,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out _))
        {
            return [];
        }

        if (!TradeInvoiceLayout.SupportsBulkInvoicePrint(companyId))
        {
            return [];
        }

        var query = _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(i => i.CompanyId == companyId
                        && i.FbrSubmittedAt != null
                        && i.Status == InvoiceStatus.Posted);

        if (fromDate.HasValue)
        {
            var from = fromDate.Value.Date;
            query = query.Where(i => i.InvoiceDate >= from);
        }

        if (toDate.HasValue)
        {
            var to = toDate.Value.Date.AddDays(1);
            query = query.Where(i => i.InvoiceDate < to);
        }

        if (!string.IsNullOrWhiteSpace(buyerName))
        {
            var buyerTerm = buyerName.Trim();
            query = query.Where(i => i.Customer.BuyerName.Contains(buyerTerm));
        }

        if (!string.IsNullOrWhiteSpace(invoiceNumber))
        {
            var invTerm = invoiceNumber.Trim();
            query = query.Where(i =>
                i.InvoiceNumber.Contains(invTerm)
                || (i.FbrInvoiceNumber != null && i.FbrInvoiceNumber.Contains(invTerm)));
        }

        return await query
            .OrderBy(i => i.Customer.BuyerName)
            .ThenBy(i => i.InvoiceDate)
            .ThenBy(i => i.InvoiceNumber)
            .Select(i => new SubmittedInvoicePrintListItemDto(
                i.Id,
                i.InvoiceNumber,
                i.Customer.BuyerName,
                i.InvoiceDate,
                i.NetTotal,
                i.FbrInvoiceNumber))
            .ToListAsync(cancellationToken);
    }

    public async Task<SalesInvoiceBulkPdfResult> GenerateBulkInvoicePdfAsync(
        IReadOnlyList<int> invoiceIds,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return new SalesInvoiceBulkPdfResult(false, companyError?.Message, null, null);
        }

        if (!TradeInvoiceLayout.SupportsBulkInvoicePrint(companyId))
        {
            return new SalesInvoiceBulkPdfResult(false, "Bulk PDF print is not enabled for this company.", null, null);
        }

        if (invoiceIds is null || invoiceIds.Count == 0)
        {
            return new SalesInvoiceBulkPdfResult(false, "Select at least one invoice to print.", null, null);
        }

        var distinctIds = invoiceIds.Distinct().ToList();
        var invoices = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(i => i.CompanyId == companyId
                        && distinctIds.Contains(i.Id)
                        && i.FbrSubmittedAt != null
                        && i.Status == InvoiceStatus.Posted)
            .Select(i => new { i.Id, i.Customer.BuyerName, i.InvoiceDate, i.InvoiceNumber })
            .ToListAsync(cancellationToken);

        if (invoices.Count != distinctIds.Count)
        {
            return new SalesInvoiceBulkPdfResult(
                false,
                "One or more selected invoices are missing or not FBR-submitted.",
                null,
                null);
        }

        var orderedIds = invoices
            .OrderBy(i => i.BuyerName)
            .ThenBy(i => i.InvoiceDate)
            .ThenBy(i => i.InvoiceNumber)
            .Select(i => i.Id)
            .ToList();

        var printModels = new List<SalesInvoicePrintDto>(orderedIds.Count);
        foreach (var id in orderedIds)
        {
            var printData = await GetPrintDataAsync(id, cancellationToken);
            if (printData is null)
            {
                return new SalesInvoiceBulkPdfResult(
                    false,
                    "Could not load print data for one or more invoices.",
                    null,
                    null);
            }

            printModels.Add(printData);
        }

        var buyerNames = invoices.Select(i => i.BuyerName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var fileName = buyerNames.Count == 1
            ? SanitizePdfFileName($"{buyerNames[0]}-invoices.pdf")
            : SanitizePdfFileName("selected-invoices.pdf");

        var pdfBytes = _salesInvoicePdfService.GenerateBulkPdf(printModels);
        return new SalesInvoiceBulkPdfResult(true, null, pdfBytes, fileName);
    }

    private static string SanitizePdfFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName
            .Select(ch => invalid.Contains(ch) ? '-' : ch)
            .ToArray())
            .Trim('-', ' ');
        return string.IsNullOrWhiteSpace(sanitized) ? "invoices.pdf" : sanitized;
    }

    private async Task<decimal> GetCustomerBalanceAsOfAsync(
        int customerId,
        DateTime asOfDate,
        CancellationToken cancellationToken)
    {
        var asOf = asOfDate.Date;

        var opening = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.Id == customerId)
            .Select(c => c.OpeningBalance)
            .FirstAsync(cancellationToken);

        var invoices = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(si => si.CustomerId == customerId
                         && si.Status == InvoiceStatus.Posted
                         && si.InvoiceDate <= asOf)
            .Select(si => new { si.InvoiceType, si.NetTotal })
            .ToListAsync(cancellationToken);

        var invoiceMovement = invoices.Sum(i =>
            i.InvoiceType == InvoiceType.CreditNote ? -i.NetTotal : i.NetTotal);

        var receiptTotal = await _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .Where(r =>
                r.CustomerId == customerId
                && r.ReceiptDate <= asOf
                && (r.PaymentMethod != PaymentMethod.Cheque
                    || (r.Status == CustomerReceiptStatus.Cleared && r.ClearedAt != null)))
            .SumAsync(r => r.Amount, cancellationToken);

        var bankEffect = await _unitOfWork.Repository<BankTransaction>()
            .Query()
            .Where(bt =>
                bt.CustomerId == customerId
                && bt.TransactionType == BankTransactionType.Withdrawal
                && !bt.IsDeleted
                && bt.JournalEntryId != null
                && bt.TransactionDate <= asOf)
            .SumAsync(bt => bt.CustomerBalanceEffect, cancellationToken);

        return opening + invoiceMovement - receiptTotal + bankEffect;
    }

    private async Task<(bool Success, string? Message, FbrSubmissionRequest? Request, string? FbrPostUrl, string? ApiToken)>
        BuildFbrSubmissionRequestAsync(
            SalesInvoice invoice,
            int companyId,
            CancellationToken cancellationToken)
    {
        var company = await _unitOfWork.Repository<Company>()
            .Query()
            .Where(c => c.Id == companyId)
            .Select(c => new
            {
                c.CompanyName,
                c.NTN,
                c.Address,
                ProvinceName = c.Province != null ? c.Province.Name : null,
                c.Phone,
                c.Email,
                c.FbrPostUrl,
                c.ApiToken
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (company is null)
        {
            return (false, "Company not found.", null, null, null);
        }

        var itemIds = invoice.Lines.Select(l => l.ItemId).Distinct().ToList();
        var itemLookup = await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => itemIds.Contains(i.Id))
            .Select(i => new { i.Id, i.ItemCode, i.ItemName, i.Description })
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        string? buyerProvince = null;
        if (invoice.ProvinceId.HasValue)
        {
            buyerProvince = await _unitOfWork.Repository<Province>()
                .Query()
                .Where(p => p.Id == invoice.ProvinceId.Value)
                .Select(p => p.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else if (invoice.Customer.ProvinceId.HasValue)
        {
            buyerProvince = await _unitOfWork.Repository<Province>()
                .Query()
                .Where(p => p.Id == invoice.Customer.ProvinceId.Value)
                .Select(p => p.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var saleType = FbrPayloadBuilder.MapSaleType(invoice.ScenarioType!.Code);
        var buyerRegistrationType = FbrPayloadBuilder.MapBuyerRegistrationType(invoice.Customer.CustomerType);
        var taxContext = await BuildTaxContextAsync(companyId, invoice.ScenarioId, cancellationToken);

        var seller = new FbrPartyDto(
            company.CompanyName,
            company.NTN,
            null,
            company.Address,
            company.ProvinceName,
            company.Phone,
            company.Email);

        var buyer = new FbrPartyDto(
            invoice.Customer.BuyerName,
            invoice.BuyerNTN ?? invoice.Customer.NTN,
            invoice.BuyerCNIC ?? invoice.Customer.CNIC,
            invoice.BuyerAddress ?? invoice.Customer.Address,
            buyerProvince,
            null,
            null,
            invoice.Customer.BuyerId);

        var taxableLines = invoice.Lines.Select(l =>
        {
            var item = itemLookup.GetValueOrDefault(l.ItemId);
            var taxable = Math.Round(Math.Max(0m, l.Quantity * l.Price - l.Discount), 2);
            var isTaxableLine = taxable > 0m
                && item is not null
                && !string.Equals(item.ItemCode, CartageItemCode, StringComparison.OrdinalIgnoreCase);
            return new { Line = l, Item = item, Taxable = taxable, IsTaxableLine = isTaxableLine };
        }).ToList();

        var goodsTaxableTotal = taxableLines
            .Where(x => x.IsTaxableLine)
            .Sum(x => x.Taxable);

        var lines = taxableLines.Select(x =>
        {
            var l = x.Line;
            var item = x.Item;
            var productDescription = FbrInvoiceLayout.BuildFbrProductDescription(
                !string.IsNullOrWhiteSpace(l.ProductDescription) ? l.ProductDescription : item?.Description,
                l.LotNo,
                l.StackNo);
            decimal salesTax;
            decimal furtherTax;
            if (taxContext.UseBillLevelTaxSplit && x.IsTaxableLine && goodsTaxableTotal > 0m)
            {
                salesTax = Math.Round(invoice.TaxAmount * x.Taxable / goodsTaxableTotal, 2);
                furtherTax = Math.Round(invoice.FurtherTax * x.Taxable / goodsTaxableTotal, 2);
            }
            else
            {
                var applyFurtherTax = SalesTaxSplit.ApplyFurtherTaxForLine(
                    taxContext.IsUnregisteredScenario && x.IsTaxableLine,
                    l.TaxRate,
                    taxContext.RegisteredRate);
                (salesTax, furtherTax, _) = SalesTaxSplit.CalculateLineTax(
                    x.Taxable,
                    taxContext.RegisteredRate,
                    taxContext.UnregisteredRate,
                    applyFurtherTax,
                    null,
                    l.TaxRate);
            }

            var fbrRate = taxContext.UseBillLevelTaxSplit && x.IsTaxableLine
                ? taxContext.RegisteredRate
                : l.TaxRate;
            var lineTaxTotal = taxContext.UseBillLevelTaxSplit && x.IsTaxableLine
                ? salesTax + furtherTax
                : l.TaxAmount;
            return new FbrSubmissionLineRequest(
                item?.ItemCode,
                l.HSCode,
                productDescription,
                l.Unit,
                l.StackNo,
                l.LotNo,
                l.Quantity,
                l.Cartons,
                l.Price,
                fbrRate,
                salesTax,
                furtherTax,
                lineTaxTotal,
                l.Discount,
                l.LineTotal,
                saleType);
        }).ToList();

        var request = new FbrSubmissionRequest(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.InvoiceDate,
            invoice.InvoiceType,
            seller,
            buyer,
            buyerRegistrationType,
            invoice.ScenarioType!.Code,
            invoice.SubTotal,
            invoice.DiscountAmount,
            invoice.TaxAmount,
            invoice.NetTotal,
            lines);

        return (true, null, request, company.FbrPostUrl, company.ApiToken);
    }

    private async Task<decimal> GetDefaultTaxRateAsync(int companyId, CancellationToken cancellationToken)
    {
        var rates = await GetCompanyTaxRatesAsync(companyId, cancellationToken);
        return rates.Registered;
    }

    private async Task<decimal> GetFallbackTaxRateAsync(
        int companyId,
        int? scenarioId,
        decimal? scenarioTaxRate,
        CancellationToken cancellationToken)
    {
        if (scenarioTaxRate.HasValue)
        {
            return scenarioTaxRate.Value;
        }

        if (!scenarioId.HasValue)
        {
            return await GetDefaultTaxRateAsync(companyId, cancellationToken);
        }

        var scenarioCode = await _unitOfWork.Repository<ScenarioType>()
            .Query()
            .Where(s => s.ScenarioId == scenarioId.Value)
            .Select(s => s.Code)
            .FirstOrDefaultAsync(cancellationToken);

        var rates = await GetCompanyTaxRatesAsync(companyId, cancellationToken);
        return string.Equals(scenarioCode, UnregisteredScenarioCode, StringComparison.OrdinalIgnoreCase)
            ? rates.Unregistered
            : rates.Registered;
    }

    private async Task<(decimal Registered, decimal Unregistered)> GetCompanyTaxRatesAsync(
        int companyId,
        CancellationToken cancellationToken)
    {
        var rates = await _unitOfWork.Repository<TaxSetting>()
            .Query()
            .Where(t => t.CompanyId == companyId)
            .Select(t => new { t.SalesTaxRate, t.UnregisteredSalesTaxRate })
            .FirstOrDefaultAsync(cancellationToken);

        return rates is null
            ? (18m, 22m)
            : (rates.SalesTaxRate, rates.UnregisteredSalesTaxRate);
    }

    private static IQueryable<SalesInvoice> ApplyOrdering(IQueryable<SalesInvoice> query, DataTableRequest request)
    {
        var desc = string.Equals(request.OrderDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return request.OrderColumn switch
        {
            0 => desc ? query.OrderByDescending(i => i.InvoiceNumber) : query.OrderBy(i => i.InvoiceNumber),
            1 => desc ? query.OrderByDescending(i => i.Customer.BuyerName) : query.OrderBy(i => i.Customer.BuyerName),
            2 => desc ? query.OrderByDescending(i => i.InvoiceDate) : query.OrderBy(i => i.InvoiceDate),
            4 => desc ? query.OrderByDescending(i => i.NetTotal) : query.OrderBy(i => i.NetTotal),
            _ => desc ? query.OrderByDescending(i => i.InvoiceDate) : query.OrderBy(i => i.InvoiceDate)
        };
    }

    private bool TryGetCompanyId(out int companyId, out SalesInvoiceSaveResult? error)
    {
        if (!_currentCompany.CompanyId.HasValue)
        {
            companyId = 0;
            error = new SalesInvoiceSaveResult(
                false,
                "No company is selected. Please choose a company from the top navbar.",
                null);
            return false;
        }

        companyId = _currentCompany.CompanyId.Value;
        error = null;
        return true;
    }

    private async Task<(
        bool Success,
        string? Message,
        int ArAccountId,
        int RevenueAccountId,
        int TaxAccountId,
        int? SalesTax18AccountId,
        int? FurtherTaxAccountId,
        int? CartageAccountId,
        int? CogsAccountId,
        int? InventoryAccountId)>
        ResolvePostingAccountsAsync(
            int companyId,
            InvoiceType invoiceType,
            CancellationToken cancellationToken)
    {
        var ar = await GetAccountIdAsync(companyId, AccountsReceivable, cancellationToken);
        var tax = await GetAccountIdAsync(companyId, SalesTaxPayable, cancellationToken);
        var salesTax18 = TradeInvoiceLayout.UsesSplitTaxSubAccounts(companyId)
            ? await GetAccountIdAsync(companyId, SalesTaxPayable18, cancellationToken)
            : null;
        var furtherTax = TradeInvoiceLayout.UsesSplitTaxSubAccounts(companyId)
            ? await GetAccountIdAsync(companyId, FurtherTaxPayable, cancellationToken)
            : null;
        var cartage = await GetAccountIdAsync(companyId, CartagePayable, cancellationToken);
        var cogs = await GetAccountIdAsync(companyId, CostOfGoodsSold, cancellationToken);
        var inventory = await GetAccountIdAsync(companyId, InventoryAsset, cancellationToken);

        var revenueNumber = invoiceType == InvoiceType.CreditNote
            ? SalesReturns
            : SalesRevenue;

        var revenue = await GetAccountIdAsync(companyId, revenueNumber, cancellationToken);

        if (ar is null)
        {
            return (false, $"Chart of account {AccountsReceivable} (Accounts Receivable) not found.", 0, 0, 0, null, null, null, null, null);
        }

        if (tax is null)
        {
            return (false, $"Chart of account {SalesTaxPayable} (Sales Tax Payable) not found.", 0, 0, 0, null, null, null, null, null);
        }

        if (revenue is null)
        {
            return (false, $"Chart of account {revenueNumber} not found.", 0, 0, 0, null, null, null, null, null);
        }

        return (true, null, ar.Value, revenue.Value, tax.Value, salesTax18, furtherTax, cartage, cogs, inventory);
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

    private static List<JournalEntryLine> BuildJournalLines(
        InvoiceType invoiceType,
        int arAccountId,
        int revenueAccountId,
        int totalTaxAccountId,
        int? salesTax18AccountId,
        int? furtherTaxAccountId,
        bool useSplitTaxSubAccounts,
        int? cartageAccountId,
        int? cogsAccountId,
        int? inventoryAccountId,
        decimal salesAmount,
        decimal salesTaxAmount,
        decimal furtherTaxAmount,
        decimal cartageAmount,
        decimal cogsAmount,
        decimal netTotal)
    {
        var lines = new List<JournalEntryLine>();
        var totalTaxAmount = Math.Round(salesTaxAmount + furtherTaxAmount, 2);

        if (invoiceType == InvoiceType.CreditNote)
        {
            lines.Add(new JournalEntryLine
            {
                ChartOfAccountId = arAccountId,
                Debit = 0m,
                Credit = netTotal,
                Memo = "Accounts Receivable"
            });

            if (salesAmount > 0m)
            {
                lines.Add(new JournalEntryLine
                {
                    ChartOfAccountId = revenueAccountId,
                    Debit = salesAmount,
                    Credit = 0m,
                    Memo = "Sales Returns"
                });
            }

            AddTaxJournalLines(
                lines,
                totalTaxAccountId,
                salesTax18AccountId,
                furtherTaxAccountId,
                useSplitTaxSubAccounts,
                salesTaxAmount,
                furtherTaxAmount,
                totalTaxAmount,
                isCreditNote: true);

            if (cartageAmount > 0m && cartageAccountId.HasValue)
            {
                lines.Add(new JournalEntryLine
                {
                    ChartOfAccountId = cartageAccountId.Value,
                    Debit = cartageAmount,
                    Credit = 0m,
                    Memo = "Cartage Payable"
                });
            }

            if (cogsAmount > 0m && cogsAccountId.HasValue && inventoryAccountId.HasValue)
            {
                lines.Add(new JournalEntryLine
                {
                    ChartOfAccountId = inventoryAccountId.Value,
                    Debit = cogsAmount,
                    Credit = 0m,
                    Memo = "Inventory Asset"
                });
                lines.Add(new JournalEntryLine
                {
                    ChartOfAccountId = cogsAccountId.Value,
                    Debit = 0m,
                    Credit = cogsAmount,
                    Memo = "Cost of Goods Sold"
                });
            }
        }
        else
        {
            lines.Add(new JournalEntryLine
            {
                ChartOfAccountId = arAccountId,
                Debit = netTotal,
                Credit = 0m,
                Memo = "Accounts Receivable"
            });

            if (salesAmount > 0m)
            {
                lines.Add(new JournalEntryLine
                {
                    ChartOfAccountId = revenueAccountId,
                    Debit = 0m,
                    Credit = salesAmount,
                    Memo = "Sales Revenue"
                });
            }

            AddTaxJournalLines(
                lines,
                totalTaxAccountId,
                salesTax18AccountId,
                furtherTaxAccountId,
                useSplitTaxSubAccounts,
                salesTaxAmount,
                furtherTaxAmount,
                totalTaxAmount,
                isCreditNote: false);

            if (cartageAmount > 0m && cartageAccountId.HasValue)
            {
                lines.Add(new JournalEntryLine
                {
                    ChartOfAccountId = cartageAccountId.Value,
                    Debit = 0m,
                    Credit = cartageAmount,
                    Memo = "Cartage Payable"
                });
            }

            if (cogsAmount > 0m && cogsAccountId.HasValue && inventoryAccountId.HasValue)
            {
                lines.Add(new JournalEntryLine
                {
                    ChartOfAccountId = cogsAccountId.Value,
                    Debit = cogsAmount,
                    Credit = 0m,
                    Memo = "Cost of Goods Sold"
                });
                lines.Add(new JournalEntryLine
                {
                    ChartOfAccountId = inventoryAccountId.Value,
                    Debit = 0m,
                    Credit = cogsAmount,
                    Memo = "Inventory Asset"
                });
            }
        }

        return lines;
    }

    private static void AddTaxJournalLines(
        List<JournalEntryLine> lines,
        int totalTaxAccountId,
        int? salesTax18AccountId,
        int? furtherTaxAccountId,
        bool useSplitTaxSubAccounts,
        decimal salesTaxAmount,
        decimal furtherTaxAmount,
        decimal totalTaxAmount,
        bool isCreditNote)
    {
        if (totalTaxAmount <= 0m)
        {
            return;
        }

        if (useSplitTaxSubAccounts)
        {
            if (salesTaxAmount > 0m && salesTax18AccountId.HasValue)
            {
                lines.Add(new JournalEntryLine
                {
                    ChartOfAccountId = salesTax18AccountId.Value,
                    Debit = isCreditNote ? salesTaxAmount : 0m,
                    Credit = isCreditNote ? 0m : salesTaxAmount,
                    Memo = "Sales Tax Payable (18%)"
                });
            }

            if (furtherTaxAmount > 0m && furtherTaxAccountId.HasValue)
            {
                lines.Add(new JournalEntryLine
                {
                    ChartOfAccountId = furtherTaxAccountId.Value,
                    Debit = isCreditNote ? furtherTaxAmount : 0m,
                    Credit = isCreditNote ? 0m : furtherTaxAmount,
                    Memo = "Further Tax Payable"
                });
            }

            return;
        }

        lines.Add(new JournalEntryLine
        {
            ChartOfAccountId = totalTaxAccountId,
            Debit = isCreditNote ? totalTaxAmount : 0m,
            Credit = isCreditNote ? 0m : totalTaxAmount,
            Memo = "Sales Tax Payable"
        });
    }

    private static bool IsCartageOrService(InvoiceItemSnapshot item) =>
        SalesTaxSplit.IsCartageOrService(item.ItemType, item.ItemCode);

    private async Task<List<int>> ReversePostedInvoiceInventoryAsync(
        int companyId,
        SalesInvoice invoice,
        CancellationToken cancellationToken)
    {
        var lineItemIds = invoice.Lines.Select(l => l.ItemId).Distinct().ToList();
        var cartageItemIds = await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => lineItemIds.Contains(i.Id)
                        && i.CompanyId == companyId
                        && i.ItemCode == CartageItemCode)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);
        var cartageItemIdSet = cartageItemIds.ToHashSet();

        var inventoryItems = await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => lineItemIds.Contains(i.Id) && i.CompanyId == companyId)
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        var goodsLines = invoice.Lines
            .Where(l => !cartageItemIdSet.Contains(l.ItemId)
                        && inventoryItems.TryGetValue(l.ItemId, out var item)
                        && item.ItemType != ItemType.Service)
            .ToList();

        if (goodsLines.Count == 0)
        {
            return [];
        }

        var isCreditNote = invoice.InvoiceType == InvoiceType.CreditNote;
        var expectedType = isCreditNote
            ? InventoryTransactionType.StockIn
            : InventoryTransactionType.StockOut;

        var candidateTransactions = await _unitOfWork.Repository<InventoryTransaction>()
            .Query(asNoTracking: false)
            .Where(t => t.CompanyId == companyId && t.ReferenceNo == invoice.InvoiceNumber)
            .ToListAsync(cancellationToken);

        var matchedItemIds = new HashSet<int>();

        foreach (var line in goodsLines)
        {
            var quantity = Math.Round(line.Quantity, 2);
            if (quantity <= 0m)
            {
                continue;
            }

            var stackNo = string.IsNullOrWhiteSpace(line.StackNo) ? null : line.StackNo.Trim();
            var lotNo = string.IsNullOrWhiteSpace(line.LotNo) ? null : line.LotNo.Trim();

            var transaction = candidateTransactions.FirstOrDefault(t =>
                t.ItemId == line.ItemId
                && t.TransactionType == expectedType
                && Math.Round(t.Quantity, 2) == quantity
                && string.Equals(t.StackNo ?? string.Empty, stackNo ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.LotNo ?? string.Empty, lotNo ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && t.TransactionDate.Date == invoice.InvoiceDate.Date);

            if (transaction is null)
            {
                continue;
            }

            _unitOfWork.Repository<InventoryTransaction>().Remove(transaction);
            matchedItemIds.Add(line.ItemId);
            candidateTransactions.Remove(transaction);
        }

        return matchedItemIds.ToList();
    }

    private async Task SoftDeleteSalesInvoiceJournalEntriesAsync(
        int companyId,
        int invoiceId,
        int? journalEntryId,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        var journalEntries = await _unitOfWork.Repository<JournalEntry>()
            .Query(asNoTracking: false)
            .Where(j => j.CompanyId == companyId
                        && !j.IsDeleted
                        && j.ReferenceType == ReferenceTypes.SalesInvoice
                        && j.ReferenceId == invoiceId)
            .ToListAsync(cancellationToken);

        if (journalEntryId.HasValue
            && journalEntries.All(j => j.Id != journalEntryId.Value))
        {
            var linkedEntry = await _unitOfWork.Repository<JournalEntry>()
                .Query(asNoTracking: false)
                .FirstOrDefaultAsync(
                    j => j.Id == journalEntryId.Value && j.CompanyId == companyId && !j.IsDeleted,
                    cancellationToken);

            if (linkedEntry is not null)
            {
                journalEntries.Add(linkedEntry);
            }
        }

        foreach (var entry in journalEntries)
        {
            entry.IsDeleted = true;
            entry.DeletedAt = now;
            entry.DeletedBy = userName;
            _unitOfWork.Repository<JournalEntry>().Update(entry);
        }
    }

    private async Task RecalculateItemStockFromTransactionsAsync(
        int companyId,
        IReadOnlyList<int> itemIds,
        DateTime now,
        string userName,
        CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0)
        {
            return;
        }

        var items = await _unitOfWork.Repository<Item>()
            .Query(asNoTracking: false)
            .Where(i => i.CompanyId == companyId && itemIds.Contains(i.Id))
            .ToListAsync(cancellationToken);

        var stockByItemId = await BuildStockByItemIdAsync(companyId, itemIds, cancellationToken);

        foreach (var item in items)
        {
            item.CurrentStock = stockByItemId.GetValueOrDefault(item.Id);
            item.UpdatedAt = now;
            item.UpdatedBy = userName;
            _unitOfWork.Repository<Item>().Update(item);
        }
    }

    private async Task<Dictionary<int, decimal>> BuildStockByItemIdAsync(
        int companyId,
        IReadOnlyList<int> itemIds,
        CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<InventoryTransaction>()
            .Query()
            .Where(t => t.CompanyId == companyId && itemIds.Contains(t.ItemId))
            .GroupBy(t => t.ItemId)
            .Select(g => new
            {
                ItemId = g.Key,
                Stock = g.Sum(t =>
                    t.TransactionType == InventoryTransactionType.StockOut
                        ? -t.Quantity
                        : t.TransactionType == InventoryTransactionType.Adjustment
                            ? t.Quantity
                            : t.Quantity)
            })
            .ToDictionaryAsync(x => x.ItemId, x => Math.Round(x.Stock, 2), cancellationToken);
    }

    private async Task<int?> GetDefaultWarehouseIdAsync(int companyId, CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<Warehouse>()
            .Query()
            .Where(w => w.CompanyId == companyId && w.IsActive)
            .OrderBy(w => w.Code)
            .Select(w => (int?)w.Id)
            .FirstOrDefaultAsync(cancellationToken);

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

    private static SalesInvoiceActionResult ToActionError(SalesInvoiceSaveResult error) =>
        new(error.Success, error.Message, null);

    private async Task TryAuditAsync(
        string action,
        string recordId,
        string? oldValue,
        string? newValue,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditService.LogAsync(action, "SalesInvoices", recordId, oldValue, newValue, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for sales invoice {RecordId}", recordId);
        }
    }

    [GeneratedRegex(@"^INV-(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex InvoiceNumberRegex();

    [GeneratedRegex(@"^JE-(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex JournalEntryNumberRegex();
}
