using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;
using System.Text.RegularExpressions;

namespace PakistanAccountingERP.Application.Services;

public partial class JournalEntryService : IJournalEntryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly ICustomerGlPostingService _customerGlPosting;
    private readonly IVendorGlPostingService _vendorGlPosting;
    private readonly IBankGlPostingService _bankGlPosting;
    private readonly ILogger<JournalEntryService> _logger;

    public JournalEntryService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IAuditService auditService,
        ICustomerGlPostingService customerGlPosting,
        IVendorGlPostingService vendorGlPosting,
        IBankGlPostingService bankGlPosting,
        ILogger<JournalEntryService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _auditService = auditService;
        _customerGlPosting = customerGlPosting;
        _vendorGlPosting = vendorGlPosting;
        _bankGlPosting = bankGlPosting;
        _logger = logger;
    }

    public async Task<DataTableResponse<JournalEntryListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var query = _unitOfWork.Repository<JournalEntry>()
            .Query()
            .Where(j => j.CompanyId == companyId);

        var recordsTotal = await query.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var term = request.SearchValue.Trim();
            query = query.Where(j =>
                j.EntryNumber.Contains(term)
                || (j.Description != null && j.Description.Contains(term))
                || (j.ReferenceType != null && j.ReferenceType.Contains(term)));
        }

        var recordsFiltered = await query.CountAsync(cancellationToken);
        query = ApplyOrdering(query, request);

        if (request.Length > 0)
        {
            query = query.Skip(request.Start).Take(request.Length);
        }

        var entries = await query
            .Select(j => new
            {
                j.Id,
                j.EntryNumber,
                j.EntryDate,
                j.Description,
                j.ReferenceType,
                j.ReferenceId,
                j.Status,
                TotalDebit = j.Lines.Sum(l => l.Debit)
            })
            .ToListAsync(cancellationToken);

        var rows = new List<JournalEntryListItemDto>();
        foreach (var entry in entries)
        {
            var sourceLabel = await ResolveSourceLabelAsync(
                companyId,
                entry.ReferenceType,
                entry.ReferenceId,
                cancellationToken);

            var isManual = IsManualEntry(entry.ReferenceType);
            var canEdit = CanEditEntry(entry.Status, isManual);
            var canDelete = CanDeleteEntry(entry.Status, isManual);
            var canRepost = CanRepostFromSource(entry.Status, isManual, entry.ReferenceId, entry.ReferenceType);
            rows.Add(new JournalEntryListItemDto(
                entry.Id,
                entry.EntryNumber,
                entry.EntryDate,
                entry.Description,
                sourceLabel,
                entry.TotalDebit,
                entry.Status.ToString(),
                entry.Status == JournalStatus.Draft && isManual,
                canDelete,
                canEdit,
                canRepost));
        }

        return new DataTableResponse<JournalEntryListItemDto>(
            request.Draw,
            recordsTotal,
            recordsFiltered,
            rows);
    }

    public async Task<JournalEntryDetailDto?> GetDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var entry = await _unitOfWork.Repository<JournalEntry>()
            .Query()
            .Where(j => j.Id == id && j.CompanyId == companyId)
            .Select(j => new
            {
                j.Id,
                j.EntryNumber,
                j.EntryDate,
                j.Description,
                j.ReferenceType,
                j.ReferenceId,
                j.Status,
                Lines = j.Lines.Select(l => new JournalEntryLineDto(
                    l.Id,
                    l.ChartOfAccountId,
                    l.ChartOfAccount.AccountNumber,
                    l.ChartOfAccount.AccountName,
                    l.Debit,
                    l.Credit,
                    l.Memo)).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (entry is null)
        {
            return null;
        }

        var sourceLabel = await ResolveSourceLabelAsync(
            companyId,
            entry.ReferenceType,
            entry.ReferenceId,
            cancellationToken);
        var sourceUrl = ResolveSourceUrl(entry.ReferenceType, entry.ReferenceId);
        var isManual = IsManualEntry(entry.ReferenceType);
        var totalDebit = entry.Lines.Sum(l => l.Debit);
        var totalCredit = entry.Lines.Sum(l => l.Credit);

        var canEdit = CanEditEntry(entry.Status, isManual);
        var canDelete = CanDeleteEntry(entry.Status, isManual);
        var canRepost = CanRepostFromSource(entry.Status, isManual, entry.ReferenceId, entry.ReferenceType);

        return new JournalEntryDetailDto(
            entry.Id,
            entry.EntryNumber,
            entry.EntryDate,
            entry.Description,
            entry.ReferenceType,
            entry.ReferenceId,
            sourceLabel,
            sourceUrl,
            entry.Status,
            totalDebit,
            totalCredit,
            entry.Status == JournalStatus.Draft && isManual,
            canDelete,
            canEdit,
            canRepost,
            isManual,
            entry.Lines);
    }

    public async Task<NextJournalEntryNumberDto> GenerateNextEntryNumberAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var prefix = AppConstants.JournalEntryNumberPrefix;

        var numbers = await _unitOfWork.Repository<JournalEntry>()
            .Query()
            .Where(j => j.CompanyId == companyId && j.EntryNumber.StartsWith(prefix))
            .Select(j => j.EntryNumber)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var number in numbers)
        {
            var match = EntryNumberRegex().Match(number);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var seq))
            {
                max = Math.Max(max, seq);
            }
        }

        return new NextJournalEntryNumberDto($"{prefix}{(max + 1):D4}");
    }

    public async Task<IReadOnlyList<JournalEntryAccountLookupDto>> GetAccountLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.IsActive)
            .OrderBy(a => a.AccountNumber)
            .Select(a => new JournalEntryAccountLookupDto(a.Id, a.AccountNumber, a.AccountName))
            .ToListAsync(cancellationToken);
    }

    public async Task<JournalEntrySaveResult> CreateAsync(
        JournalEntrySaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var lineBuild = await BuildLineEntitiesAsync(request.Lines, companyId, cancellationToken);
        if (!lineBuild.Success)
        {
            return new JournalEntrySaveResult(false, lineBuild.Message, null);
        }

        var entryNumber = string.IsNullOrWhiteSpace(request.EntryNumber)
            ? (await GenerateNextEntryNumberAsync(cancellationToken)).EntryNumber
            : request.EntryNumber.Trim();

        var numberExists = await _unitOfWork.Repository<JournalEntry>()
            .Query()
            .AnyAsync(j => j.CompanyId == companyId && j.EntryNumber == entryNumber, cancellationToken);

        if (numberExists)
        {
            return new JournalEntrySaveResult(false, "Journal entry number already exists.", null);
        }

        var now = DateTime.UtcNow;
        var entity = new JournalEntry
        {
            CompanyId = companyId,
            EntryNumber = entryNumber,
            EntryDate = request.EntryDate.Date,
            Description = request.Description?.Trim(),
            ReferenceType = ReferenceTypes.Manual,
            ReferenceId = null,
            Status = JournalStatus.Draft,
            CreatedAt = now,
            CreatedBy = _currentUser.UserName ?? "system"
        };

        try
        {
            await _unitOfWork.Repository<JournalEntry>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            foreach (var line in lineBuild.Lines)
            {
                line.JournalEntryId = entity.Id;
            }

            await _unitOfWork.Repository<JournalEntryLine>().AddRangeAsync(lineBuild.Lines, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to create journal entry {EntryNumber}", entryNumber);
            return new JournalEntrySaveResult(false, "Could not save journal entry.", null);
        }

        try
        {
            await _auditService.LogAsync("Create", "JournalEntries", entity.Id.ToString(), null, entryNumber, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for journal entry {EntryId}", entity.Id);
        }

        return new JournalEntrySaveResult(true, null, entity.Id);
    }

    public async Task<JournalEntrySaveResult> UpdateAsync(
        int id,
        JournalEntrySaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var entry = await _unitOfWork.Repository<JournalEntry>()
            .Query(asNoTracking: false)
            .Include(j => j.Lines)
            .FirstOrDefaultAsync(j => j.Id == id && j.CompanyId == companyId, cancellationToken);

        if (entry is null)
        {
            return new JournalEntrySaveResult(false, "Journal entry not found.", null);
        }

        var isManual = IsManualEntry(entry.ReferenceType);
        if (!CanEditEntry(entry.Status, isManual))
        {
            return new JournalEntrySaveResult(false, GetEditDeniedMessage(entry.Status, isManual), null);
        }

        var lineBuild = await BuildLineEntitiesAsync(request.Lines, companyId, cancellationToken);
        if (!lineBuild.Success)
        {
            return new JournalEntrySaveResult(false, lineBuild.Message, null);
        }

        var oldSnapshot = entry.EntryNumber;
        var now = DateTime.UtcNow;

        entry.EntryDate = request.EntryDate.Date;
        entry.Description = request.Description?.Trim();
        entry.UpdatedAt = now;
        entry.UpdatedBy = _currentUser.UserName;

        _unitOfWork.Repository<JournalEntryLine>().RemoveRange(entry.Lines);

        try
        {
            _unitOfWork.Repository<JournalEntry>().Update(entry);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            foreach (var line in lineBuild.Lines)
            {
                line.JournalEntryId = entry.Id;
            }

            await _unitOfWork.Repository<JournalEntryLine>().AddRangeAsync(lineBuild.Lines, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to update journal entry {EntryId}", id);
            return new JournalEntrySaveResult(false, "Could not save journal entry.", null);
        }

        try
        {
            await _auditService.LogAsync("Update", "JournalEntries", id.ToString(), oldSnapshot, entry.EntryNumber, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for journal entry {EntryId}", id);
        }

        return new JournalEntrySaveResult(true, null, entry.Id);
    }

    public async Task<JournalEntryActionResult> PostAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return ToActionError(companyError!);
        }

        var entry = await _unitOfWork.Repository<JournalEntry>()
            .Query(asNoTracking: false)
            .Include(j => j.Lines)
            .FirstOrDefaultAsync(j => j.Id == id && j.CompanyId == companyId, cancellationToken);

        if (entry is null)
        {
            return new JournalEntryActionResult(false, "Journal entry not found.", null);
        }

        if (entry.Status != JournalStatus.Draft)
        {
            return new JournalEntryActionResult(false, "Only draft entries can be posted.", null);
        }

        if (!IsManualEntry(entry.ReferenceType))
        {
            return new JournalEntryActionResult(false, "System-generated entries cannot be posted from here.", null);
        }

        var totalDebit = entry.Lines.Sum(l => l.Debit);
        var totalCredit = entry.Lines.Sum(l => l.Credit);
        if (Math.Abs(totalDebit - totalCredit) > 0.01m)
        {
            return new JournalEntryActionResult(false, "Debits and credits must balance before posting.", null);
        }

        entry.Status = JournalStatus.Posted;
        entry.UpdatedAt = DateTime.UtcNow;
        entry.UpdatedBy = _currentUser.UserName;

        _unitOfWork.Repository<JournalEntry>().Update(entry);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await _auditService.LogAsync(
                "Post",
                "JournalEntries",
                id.ToString(),
                JournalStatus.Draft.ToString(),
                JournalStatus.Posted.ToString(),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for journal entry {EntryId}", id);
        }

        var detail = await GetDetailAsync(id, cancellationToken);
        return new JournalEntryActionResult(true, "Journal entry posted to the general ledger.", detail);
    }

    public async Task<JournalEntryActionResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return ToActionError(companyError!);
        }

        var entry = await _unitOfWork.Repository<JournalEntry>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(j => j.Id == id && j.CompanyId == companyId, cancellationToken);

        if (entry is null)
        {
            return new JournalEntryActionResult(false, "Journal entry not found.", null);
        }

        if (!CanDeleteEntry(entry.Status, IsManualEntry(entry.ReferenceType)))
        {
            return new JournalEntryActionResult(
                false,
                GetDeleteDeniedMessage(entry.Status, IsManualEntry(entry.ReferenceType)),
                null);
        }

        entry.IsDeleted = true;
        entry.DeletedAt = DateTime.UtcNow;
        entry.DeletedBy = _currentUser.UserName;

        _unitOfWork.Repository<JournalEntry>().Update(entry);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await _auditService.LogAsync("Delete", "JournalEntries", id.ToString(), entry.EntryNumber, null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for journal entry {EntryId}", id);
        }

        return new JournalEntryActionResult(true, GetDeleteSuccessMessage(entry.Status), null);
    }

    public async Task<JournalEntryActionResult> RepostFromSourceAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return ToActionError(companyError!);
        }

        var entry = await _unitOfWork.Repository<JournalEntry>()
            .Query()
            .Where(j => j.Id == id && j.CompanyId == companyId)
            .Select(j => new { j.Id, j.EntryNumber, j.ReferenceType, j.ReferenceId, j.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (entry is null)
        {
            return new JournalEntryActionResult(false, "Journal entry not found.", null);
        }

        var isManual = IsManualEntry(entry.ReferenceType);
        if (!CanRepostFromSource(entry.Status, isManual, entry.ReferenceId, entry.ReferenceType))
        {
            return new JournalEntryActionResult(
                false,
                "This journal entry cannot be reposted from its source document.",
                null);
        }

        var repostResult = await RepostSourceDocumentAsync(
            companyId,
            entry.ReferenceType!,
            entry.ReferenceId!.Value,
            cancellationToken);

        if (!repostResult.Success)
        {
            return new JournalEntryActionResult(false, repostResult.Message, null);
        }

        try
        {
            await _auditService.LogAsync(
                "RepostFromSource",
                "JournalEntries",
                id.ToString(),
                entry.EntryNumber,
                entry.ReferenceType,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for journal entry repost {EntryId}", id);
        }

        var newEntryId = await _unitOfWork.Repository<JournalEntry>()
            .Query()
            .Where(j =>
                j.CompanyId == companyId
                && j.ReferenceType == entry.ReferenceType
                && j.ReferenceId == entry.ReferenceId
                && !j.IsDeleted)
            .OrderByDescending(j => j.Id)
            .Select(j => j.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var detail = await GetDetailAsync(newEntryId > 0 ? newEntryId : id, cancellationToken);
        return new JournalEntryActionResult(
            true,
            "Journal entry reposted from the source document.",
            detail);
    }

    private async Task<(bool Success, string? Message)> RepostSourceDocumentAsync(
        int companyId,
        string referenceType,
        int referenceId,
        CancellationToken cancellationToken)
    {
        switch (referenceType)
        {
            case ReferenceTypes.CustomerReceipt:
            {
                var receipt = await _unitOfWork.Repository<CustomerReceipt>()
                    .Query()
                    .FirstOrDefaultAsync(r => r.Id == referenceId && r.CompanyId == companyId, cancellationToken);
                if (receipt is null)
                {
                    return (false, "Customer receipt not found.");
                }

                var result = await _customerGlPosting.PostCustomerReceiptAsync(receipt, cancellationToken: cancellationToken);
                return (result.Success, result.Message);
            }

            case ReferenceTypes.VendorPayment:
            {
                var payment = await _unitOfWork.Repository<VendorPayment>()
                    .Query()
                    .FirstOrDefaultAsync(p => p.Id == referenceId && p.CompanyId == companyId, cancellationToken);
                if (payment is null)
                {
                    return (false, "Vendor payment not found.");
                }

                var result = await _vendorGlPosting.PostVendorPaymentAsync(payment, cancellationToken);
                return (result.Success, result.Message);
            }

            case ReferenceTypes.BankTransaction:
            {
                var transaction = await _unitOfWork.Repository<BankTransaction>()
                    .Query()
                    .FirstOrDefaultAsync(t => t.Id == referenceId && t.CompanyId == companyId, cancellationToken);
                if (transaction is null)
                {
                    return (false, "Bank transaction not found.");
                }

                var result = await _bankGlPosting.PostBankTransactionAsync(transaction, cancellationToken);
                return (result.Success, result.Message);
            }

            case ReferenceTypes.Customer:
            {
                var customer = await _unitOfWork.Repository<Customer>()
                    .Query()
                    .Where(c => c.Id == referenceId && c.CompanyId == companyId)
                    .Select(c => new { c.BuyerName, c.OpeningBalance })
                    .FirstOrDefaultAsync(cancellationToken);
                if (customer is null)
                {
                    return (false, "Customer not found.");
                }

                var result = await _customerGlPosting.SyncCustomerOpeningBalanceAsync(
                    referenceId,
                    customer.BuyerName,
                    customer.OpeningBalance,
                    cancellationToken: cancellationToken);
                return (result.Success, result.Message);
            }

            case ReferenceTypes.Vendor:
            {
                var vendor = await _unitOfWork.Repository<Vendor>()
                    .Query()
                    .Where(v => v.Id == referenceId && v.CompanyId == companyId)
                    .Select(v => new { v.VendorName, v.OpeningBalance })
                    .FirstOrDefaultAsync(cancellationToken);
                if (vendor is null)
                {
                    return (false, "Vendor not found.");
                }

                var result = await _vendorGlPosting.SyncVendorOpeningBalanceAsync(
                    referenceId,
                    vendor.VendorName,
                    vendor.OpeningBalance,
                    cancellationToken: cancellationToken);
                return (result.Success, result.Message);
            }

            default:
                return (false, "Repost is not supported for this source type. Edit the source document instead.");
        }
    }

    private static string GetDeleteSuccessMessage(JournalStatus status) =>
        status == JournalStatus.Posted
            ? "Posted journal entry removed from the general ledger."
            : "Journal entry deleted.";

    private async Task<(bool Success, string? Message, List<JournalEntryLine> Lines)> BuildLineEntitiesAsync(
        IReadOnlyList<JournalEntryLineSaveRequest> lines,
        int companyId,
        CancellationToken cancellationToken)
    {
        if (lines.Count < 2)
        {
            return (false, "Add at least two journal lines.", []);
        }

        var accountIds = lines.Select(l => l.ChartOfAccountId).Distinct().ToList();
        var validAccounts = await _unitOfWork.Repository<ChartOfAccount>()
            .Query()
            .Where(a => a.CompanyId == companyId && a.IsActive && accountIds.Contains(a.Id))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        if (validAccounts.Count != accountIds.Count)
        {
            return (false, "One or more accounts are invalid.", []);
        }

        var entities = new List<JournalEntryLine>();
        decimal totalDebit = 0m;
        decimal totalCredit = 0m;

        foreach (var line in lines)
        {
            var debit = Math.Round(Math.Max(0m, line.Debit), 2);
            var credit = Math.Round(Math.Max(0m, line.Credit), 2);

            if (debit == 0m && credit == 0m)
            {
                return (false, "Each line must have a debit or credit amount.", []);
            }

            if (debit > 0m && credit > 0m)
            {
                return (false, "A line cannot have both debit and credit amounts.", []);
            }

            totalDebit += debit;
            totalCredit += credit;

            entities.Add(new JournalEntryLine
            {
                ChartOfAccountId = line.ChartOfAccountId,
                Debit = debit,
                Credit = credit,
                Memo = line.Memo?.Trim()
            });
        }

        if (Math.Abs(totalDebit - totalCredit) > 0.01m)
        {
            return (false, "Total debits must equal total credits.", []);
        }

        if (totalDebit == 0m)
        {
            return (false, "Journal entry total must be greater than zero.", []);
        }

        return (true, null, entities);
    }

    private async Task<string> ResolveSourceLabelAsync(
        int companyId,
        string? referenceType,
        int? referenceId,
        CancellationToken cancellationToken)
    {
        if (IsManualEntry(referenceType))
        {
            return "Manual Entry";
        }

        if (!referenceId.HasValue || string.IsNullOrWhiteSpace(referenceType))
        {
            return "—";
        }

        if (referenceType == ReferenceTypes.SalesInvoice)
        {
            var number = await _unitOfWork.Repository<SalesInvoice>()
                .Query()
                .Where(i => i.Id == referenceId.Value && i.CompanyId == companyId)
                .Select(i => i.InvoiceNumber)
                .FirstOrDefaultAsync(cancellationToken);

            return number is null ? "Sales Invoice" : $"Sales Invoice {number}";
        }

        if (referenceType == ReferenceTypes.VendorBill)
        {
            var number = await _unitOfWork.Repository<VendorBill>()
                .Query()
                .Where(b => b.Id == referenceId.Value && b.CompanyId == companyId)
                .Select(b => b.BillNumber)
                .FirstOrDefaultAsync(cancellationToken);

            return number is null ? "Vendor Bill" : $"Vendor Bill {number}";
        }

        if (referenceType == ReferenceTypes.CustomerReceipt)
        {
            var number = await _unitOfWork.Repository<CustomerReceipt>()
                .Query()
                .Where(r => r.Id == referenceId.Value && r.CompanyId == companyId)
                .Select(r => r.ReceiptNumber)
                .FirstOrDefaultAsync(cancellationToken);

            return number is null ? "Customer Receipt" : $"Customer Receipt {number}";
        }

        return referenceType;
    }

    private static string? ResolveSourceUrl(string? referenceType, int? referenceId)
    {
        if (!referenceId.HasValue || string.IsNullOrWhiteSpace(referenceType))
        {
            return null;
        }

        return referenceType switch
        {
            ReferenceTypes.SalesInvoice => $"/SalesInvoices/Details/{referenceId.Value}",
            ReferenceTypes.VendorBill => $"/VendorBills/Details/{referenceId.Value}",
            ReferenceTypes.CustomerReceipt => $"/CustomerReceipts",
            ReferenceTypes.VendorPayment => $"/VendorPayments",
            ReferenceTypes.BankTransaction => $"/BankTransactions",
            _ => null
        };
    }

    private static bool IsManualEntry(string? referenceType) =>
        string.IsNullOrWhiteSpace(referenceType) || referenceType == ReferenceTypes.Manual;

    private bool IsSuperAdmin() =>
        _currentUser.Roles.Any(r => string.Equals(r, "SuperAdmin", StringComparison.OrdinalIgnoreCase));

    private bool CanEditEntry(JournalStatus status, bool isManual) =>
        status is JournalStatus.Draft or JournalStatus.Posted
        && (isManual || IsSuperAdmin());

    private static bool CanRepostFromSource(
        JournalStatus status,
        bool isManual,
        int? referenceId,
        string? referenceType) =>
        status == JournalStatus.Posted
        && !isManual
        && referenceId.HasValue
        && SupportsRepostFromSource(referenceType);

    private static bool SupportsRepostFromSource(string? referenceType) =>
        referenceType is ReferenceTypes.CustomerReceipt
            or ReferenceTypes.VendorPayment
            or ReferenceTypes.BankTransaction
            or ReferenceTypes.Customer
            or ReferenceTypes.Vendor;

    private static bool CanDeleteEntry(JournalStatus status, bool isManual) =>
        isManual
        && status is JournalStatus.Draft or JournalStatus.Posted;

    private static string GetEditDeniedMessage(JournalStatus status, bool isManual)
    {
        if (status == JournalStatus.Reversed)
        {
            return "Reversed journal entries cannot be edited.";
        }

        if (!isManual)
        {
            return "This entry was created from a receipt, payment, or other document. Use Repost from source on the details page, or edit the source document. SuperAdmin can edit lines directly.";
        }

        return "This journal entry cannot be edited.";
    }

    private static string GetDeleteDeniedMessage(JournalStatus status, bool isManual)
    {
        if (!isManual)
        {
            return "System-generated journal entries cannot be deleted. Delete or reverse the source document instead.";
        }

        if (status == JournalStatus.Reversed)
        {
            return "Reversed journal entries cannot be deleted.";
        }

        return "This journal entry cannot be deleted.";
    }

    private static JournalEntryActionResult ToActionError(JournalEntrySaveResult error) =>
        new(error.Success, error.Message, null);

    private bool TryGetCompanyId(out int companyId, out JournalEntrySaveResult? error)
    {
        if (!_currentCompany.CompanyId.HasValue)
        {
            companyId = 0;
            error = new JournalEntrySaveResult(
                false,
                "No company is selected. Please choose a company from the top navbar.",
                null);
            return false;
        }

        companyId = _currentCompany.CompanyId.Value;
        error = null;
        return true;
    }

    private static IQueryable<JournalEntry> ApplyOrdering(IQueryable<JournalEntry> query, DataTableRequest request)
    {
        var desc = string.Equals(request.OrderDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return request.OrderColumn switch
        {
            0 => desc ? query.OrderByDescending(j => j.EntryNumber) : query.OrderBy(j => j.EntryNumber),
            1 => desc ? query.OrderByDescending(j => j.EntryDate) : query.OrderBy(j => j.EntryDate),
            2 => desc ? query.OrderByDescending(j => j.Description) : query.OrderBy(j => j.Description),
            4 => desc ? query.OrderByDescending(j => j.Status) : query.OrderBy(j => j.Status),
            _ => desc ? query.OrderByDescending(j => j.EntryDate) : query.OrderBy(j => j.EntryDate)
        };
    }

    [GeneratedRegex(@"^JE-(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex EntryNumberRegex();
}
