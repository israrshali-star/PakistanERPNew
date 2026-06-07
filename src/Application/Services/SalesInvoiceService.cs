using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.Common.Constants;
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
    private readonly ILogger<SalesInvoiceService> _logger;

    private const string AccountsReceivableNumber = "1200";
    private const string SalesTaxPayableNumber = "2200";
    private const string SalesRevenueNumber = "4100";
    private const string SalesReturnsNumber = "4200";

    public SalesInvoiceService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IAuditService auditService,
        IFbrSubmissionService fbrSubmissionService,
        IStackLotInventoryService stackLotInventory,
        ILogger<SalesInvoiceService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _auditService = auditService;
        _fbrSubmissionService = fbrSubmissionService;
        _stackLotInventory = stackLotInventory;
        _logger = logger;
    }

    public async Task<DataTableResponse<SalesInvoiceListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var query = _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(i => i.CompanyId == companyId);

        var recordsTotal = await query.CountAsync(cancellationToken);

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
                i.Customer.BuyerName,
                i.InvoiceDate,
                i.NetTotal,
                i.Status.ToString(),
                i.FbrInvoiceNumber,
                i.Status == InvoiceStatus.Draft,
                i.Status == InvoiceStatus.Posted && i.FbrSubmittedAt == null,
                i.FbrSubmittedAt != null,
                i.Status != InvoiceStatus.Cancelled))
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
                c.Address,
                c.NTN,
                c.CNIC,
                c.InvoiceType))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SalesInvoiceItemLookupDto>> GetItemLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var defaultTaxRate = await GetDefaultTaxRateAsync(companyId, cancellationToken);

        return await _unitOfWork.Repository<Item>()
            .Query()
            .Where(i => i.CompanyId == companyId && i.IsActive)
            .OrderBy(i => i.ItemName)
            .Select(i => new SalesInvoiceItemLookupDto(
                i.Id,
                i.ItemCode,
                i.ItemName,
                i.Description,
                i.HSCode,
                i.StackNo,
                i.LotNo,
                i.UnitOfMeasure.Symbol ?? "PCS",
                i.SaleRate,
                defaultTaxRate))
            .ToListAsync(cancellationToken);
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
                UnitSymbol = i.UnitOfMeasure.Symbol ?? "PCS"
            })
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        if (items.Count != itemIds.Count)
        {
            return new SalesInvoiceSaveResult(false, "One or more items are invalid.", null);
        }

        var validationLines = new List<StackLotSaleValidationLine>();
        var lineEntities = new List<SalesInvoiceLine>();
        decimal subTotal = 0m;
        decimal discountTotal = 0m;
        decimal taxTotal = 0m;

        foreach (var line in request.Lines)
        {
            if (line.ItemId <= 0 || line.Quantity <= 0 || line.Price < 0)
            {
                return new SalesInvoiceSaveResult(false, "Each line needs an item, quantity, and price.", null);
            }

            var item = items[line.ItemId];
            var stackNo = string.IsNullOrWhiteSpace(line.StackNo) ? item.StackNo : line.StackNo.Trim();
            var lotNo = string.IsNullOrWhiteSpace(line.LotNo) ? item.LotNo : line.LotNo.Trim();

            validationLines.Add(new StackLotSaleValidationLine(
                line.ItemId,
                item.ItemCode,
                string.IsNullOrWhiteSpace(stackNo) ? null : stackNo,
                string.IsNullOrWhiteSpace(lotNo) ? null : lotNo,
                line.Quantity,
                Math.Max(0m, line.Cartons)));
            var lineSubTotal = Math.Round(line.Quantity * line.Price, 2);
            var lineDiscount = Math.Round(Math.Max(0m, line.Discount), 2);
            var taxable = Math.Round(lineSubTotal - lineDiscount, 2);
            var lineTax = Math.Round(taxable * Math.Max(0m, line.TaxRate) / 100m, 2);
            var lineTotal = Math.Round(taxable + lineTax, 2);

            subTotal += lineSubTotal;
            discountTotal += lineDiscount;
            taxTotal += lineTax;

            var description = !string.IsNullOrWhiteSpace(item.Description)
                ? item.Description.Trim()
                : item.ItemName;

            lineEntities.Add(new SalesInvoiceLine
            {
                ItemId = line.ItemId,
                HSCode = item.HSCode,
                ProductDescription = $"{item.ItemCode} — {description}",
                Unit = item.UnitSymbol,
                StackNo = string.IsNullOrWhiteSpace(stackNo) ? null : stackNo,
                LotNo = string.IsNullOrWhiteSpace(lotNo) ? null : lotNo,
                Quantity = line.Quantity,
                Cartons = Math.Max(0m, line.Cartons),
                Price = line.Price,
                TaxRate = line.TaxRate,
                TaxAmount = lineTax,
                Discount = lineDiscount,
                LineTotal = lineTotal
            });
        }

        var netTotal = Math.Round(subTotal - discountTotal + taxTotal, 2);

        var stockValidation = await _stackLotInventory.ValidateSaleLinesAsync(
            request.InvoiceType,
            validationLines,
            excludeInvoiceId: null,
            cancellationToken);

        if (!stockValidation.Success)
        {
            return new SalesInvoiceSaveResult(false, stockValidation.Message, null);
        }

        var now = DateTime.UtcNow;

        var entity = new SalesInvoice
        {
            CompanyId = companyId,
            InvoiceNumber = invoiceNumber,
            CustomerId = customer.Id,
            BuyerAddress = request.BuyerAddress?.Trim() ?? customer.Address,
            ProvinceId = request.ProvinceId ?? customer.ProvinceId,
            BuyerNTN = request.BuyerNTN?.Trim() ?? customer.NTN,
            BuyerCNIC = request.BuyerCNIC?.Trim() ?? customer.CNIC,
            InvoiceDate = request.InvoiceDate.Date,
            InvoiceType = request.InvoiceType,
            ScenarioId = request.ScenarioId ?? customer.ScenarioId,
            SubTotal = subTotal,
            DiscountAmount = discountTotal,
            TaxAmount = taxTotal,
            NetTotal = netTotal,
            Status = InvoiceStatus.Draft,
            CreatedAt = now,
            CreatedBy = _currentUser.UserName ?? "system"
        };

        try
        {
            await _unitOfWork.Repository<SalesInvoice>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            foreach (var line in lineEntities)
            {
                line.SalesInvoiceId = entity.Id;
            }

            await _unitOfWork.Repository<SalesInvoiceLine>().AddRangeAsync(lineEntities, cancellationToken);
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

    public async Task<SalesInvoiceDetailDto?> GetDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(i => i.Id == id && i.CompanyId == companyId)
            .Select(i => new SalesInvoiceDetailDto(
                i.Id,
                i.InvoiceNumber,
                i.CustomerId,
                i.Customer.BuyerName,
                i.Customer.BuyerId,
                i.InvoiceDate,
                i.InvoiceType,
                i.ScenarioId,
                i.ScenarioType != null ? i.ScenarioType.Code : null,
                i.BuyerAddress,
                i.Province != null
                    ? i.Province.Name
                    : i.Customer.Province != null
                        ? i.Customer.Province.Name
                        : null,
                i.BuyerNTN,
                i.BuyerCNIC,
                i.Company.CompanyName,
                i.Company.NTN,
                i.Company.Address,
                i.Company.Province != null ? i.Company.Province.Name : null,
                i.Company.Phone,
                i.Company.Email,
                i.SubTotal,
                i.DiscountAmount,
                i.TaxAmount,
                i.NetTotal,
                i.Status,
                i.FbrInvoiceNumber,
                i.FbrSubmittedAt,
                i.JournalEntryId,
                i.JournalEntry != null ? i.JournalEntry.EntryNumber : null,
                i.FbrSubmittedAt != null,
                i.Lines.Select(l => new SalesInvoiceLineDto(
                    l.Id,
                    l.Item.ItemCode,
                    l.Item.ItemName,
                    l.HSCode,
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
                    l.LineTotal)).ToList()))
            .FirstOrDefaultAsync(cancellationToken);
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

        var salesAmount = Math.Round(invoice.SubTotal - invoice.DiscountAmount, 2);
        var taxAmount = Math.Round(invoice.TaxAmount, 2);
        var netTotal = Math.Round(invoice.NetTotal, 2);

        if (salesAmount + taxAmount != netTotal)
        {
            return new SalesInvoiceActionResult(false, "Invoice totals are inconsistent. Cannot post.", null);
        }

        var journalLines = BuildJournalLines(
            invoice.InvoiceType,
            accounts.ArAccountId,
            accounts.RevenueAccountId,
            accounts.TaxAccountId,
            salesAmount,
            taxAmount,
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

            invoice.Status = InvoiceStatus.Posted;
            invoice.JournalEntryId = journalEntry.Id;
            invoice.UpdatedAt = now;
            invoice.UpdatedBy = userName;

            _unitOfWork.Repository<SalesInvoice>().Update(invoice);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
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
                    l.Item.ItemName,
                    l.HSCode,
                    l.ProductDescription,
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

        var saleType = FbrPayloadBuilder.MapSaleType(invoice.ScenarioCode);
        var exclusiveTotal = Math.Round(invoice.SubTotal - invoice.DiscountAmount, 2);
        var lines = invoice.Lines.Select((l, index) =>
        {
            var valueExcludingSt = Math.Round(Math.Max(0m, l.Quantity * l.Price - l.Discount), 2);
            return new SalesInvoicePrintLineDto(
                index + 1,
                FbrInvoiceLayout.BuildProductLine(l.ProductDescription, l.ItemName, l.LotNo, l.StackNo),
                l.HSCode,
                saleType,
                Math.Round(l.Quantity, 2),
                l.Unit,
                FbrInvoiceLayout.FormatTaxRate(l.TaxRate),
                valueExcludingSt,
                Math.Round(l.TaxAmount, 2),
                0m,
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
            .Select(i => new { i.Id, i.ItemCode, i.ItemName })
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

        var lines = invoice.Lines.Select(l =>
        {
            var item = itemLookup.GetValueOrDefault(l.ItemId);
            return new FbrSubmissionLineRequest(
                item?.ItemCode,
                l.HSCode,
                l.ProductDescription ?? item?.ItemName ?? "Item",
                l.Unit,
                l.StackNo,
                l.LotNo,
                l.Quantity,
                l.Cartons,
                l.Price,
                l.TaxRate,
                l.TaxAmount,
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
        var rate = await _unitOfWork.Repository<TaxSetting>()
            .Query()
            .Where(t => t.CompanyId == companyId)
            .Select(t => (decimal?)t.SalesTaxRate)
            .FirstOrDefaultAsync(cancellationToken);

        return rate ?? 18m;
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

    private async Task<(bool Success, string? Message, int ArAccountId, int RevenueAccountId, int TaxAccountId)>
        ResolvePostingAccountsAsync(
            int companyId,
            InvoiceType invoiceType,
            CancellationToken cancellationToken)
    {
        var ar = await GetAccountIdAsync(companyId, AccountsReceivableNumber, cancellationToken);
        var tax = await GetAccountIdAsync(companyId, SalesTaxPayableNumber, cancellationToken);

        var revenueNumber = invoiceType == InvoiceType.CreditNote
            ? SalesReturnsNumber
            : SalesRevenueNumber;

        var revenue = await GetAccountIdAsync(companyId, revenueNumber, cancellationToken);

        if (ar is null)
        {
            return (false, $"Chart of account {AccountsReceivableNumber} (Accounts Receivable) not found.", 0, 0, 0);
        }

        if (tax is null)
        {
            return (false, $"Chart of account {SalesTaxPayableNumber} (Sales Tax Payable) not found.", 0, 0, 0);
        }

        if (revenue is null)
        {
            return (false, $"Chart of account {revenueNumber} not found.", 0, 0, 0);
        }

        return (true, null, ar.Value, revenue.Value, tax.Value);
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
        int taxAccountId,
        decimal salesAmount,
        decimal taxAmount,
        decimal netTotal)
    {
        var lines = new List<JournalEntryLine>();

        if (invoiceType == InvoiceType.CreditNote)
        {
            lines.Add(new JournalEntryLine
            {
                ChartOfAccountId = arAccountId,
                Debit = 0m,
                Credit = netTotal,
                Memo = "Accounts Receivable"
            });
            lines.Add(new JournalEntryLine
            {
                ChartOfAccountId = revenueAccountId,
                Debit = salesAmount,
                Credit = 0m,
                Memo = "Sales Returns"
            });
            lines.Add(new JournalEntryLine
            {
                ChartOfAccountId = taxAccountId,
                Debit = taxAmount,
                Credit = 0m,
                Memo = "Sales Tax Payable"
            });
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
            lines.Add(new JournalEntryLine
            {
                ChartOfAccountId = revenueAccountId,
                Debit = 0m,
                Credit = salesAmount,
                Memo = "Sales Revenue"
            });
            lines.Add(new JournalEntryLine
            {
                ChartOfAccountId = taxAccountId,
                Debit = 0m,
                Credit = taxAmount,
                Memo = "Sales Tax Payable"
            });
        }

        return lines;
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
