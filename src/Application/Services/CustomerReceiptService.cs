using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.Common.Constants;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PakistanAccountingERP.Application.Services;

public partial class CustomerReceiptService : ICustomerReceiptService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly ICustomerGlPostingService _customerGlPosting;
    private readonly IBankGlPostingService _bankGlPosting;
    private readonly IBankService _bankService;
    private readonly ILogger<CustomerReceiptService> _logger;

    public CustomerReceiptService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IAuditService auditService,
        ICustomerGlPostingService customerGlPosting,
        IBankGlPostingService bankGlPosting,
        IBankService bankService,
        ILogger<CustomerReceiptService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _auditService = auditService;
        _customerGlPosting = customerGlPosting;
        _bankGlPosting = bankGlPosting;
        _bankService = bankService;
        _logger = logger;
    }

    public async Task<DataTableResponse<CustomerReceiptListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var query = _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .Where(r => r.CompanyId == companyId);

        var recordsTotal = await query.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var term = request.SearchValue.Trim();
            query = query.Where(r =>
                r.ReceiptNumber.Contains(term)
                || r.Customer.BuyerName.Contains(term)
                || r.Customer.BuyerId.Contains(term)
                || (r.ChequeNumber != null && r.ChequeNumber.Contains(term)));
        }

        var recordsFiltered = await query.CountAsync(cancellationToken);
        query = ApplyOrdering(query, request);

        if (request.Length > 0)
        {
            query = query.Skip(request.Start).Take(request.Length);
        }

        var today = DateTime.Today;
        var rows = await query
            .Select(r => new CustomerReceiptListItemDto(
                r.Id,
                r.ReceiptNumber,
                r.Customer.BuyerName,
                r.ReceiptDate,
                r.Amount,
                r.PaymentMethod == PaymentMethod.Cheque
                    ? "Cheque"
                    : r.PaymentMethod == PaymentMethod.BankTransfer
                        ? "Bank Transfer"
                        : "Cash",
                r.Bank != null ? r.Bank.BankName : null,
                r.ChequeNumber,
                r.ChequeDate,
                GetDepositStatusLabel(
                    r.PaymentMethod,
                    r.ChequeBankType,
                    r.Status,
                    r.ClearedAt,
                    r.IsDeposited,
                    r.ChequeDate,
                    today)))
            .ToListAsync(cancellationToken);

        return new DataTableResponse<CustomerReceiptListItemDto>(
            request.Draw,
            recordsTotal,
            recordsFiltered,
            rows);
    }

    public async Task<CustomerReceiptDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .Where(r => r.Id == id && r.CompanyId == companyId)
            .Select(r => new CustomerReceiptDto(
                r.Id,
                r.ReceiptNumber,
                r.CustomerId,
                r.Customer.BuyerName,
                r.Customer.BuyerId,
                r.ReceiptDate,
                r.Amount,
                r.PaymentMethod,
                r.ChequeBankType,
                r.BankId,
                r.Bank != null ? r.Bank.BankName : null,
                r.ChequeNumber,
                r.ChequeDate,
                r.Notes,
                r.Status,
                r.IsDeposited,
                r.ClearedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<NextReceiptNumberDto> GenerateNextReceiptNumberAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var prefix = AppConstants.ReceiptNumberPrefix;

        var numbers = await _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .Where(r => r.CompanyId == companyId && r.ReceiptNumber.StartsWith(prefix))
            .Select(r => r.ReceiptNumber)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var number in numbers)
        {
            var match = ReceiptNumberRegex().Match(number);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var seq))
            {
                max = Math.Max(max, seq);
            }
        }

        return new NextReceiptNumberDto($"{prefix}{(max + 1):D4}");
    }

    public async Task<IReadOnlyList<CustomerReceiptCustomerLookupDto>> GetCustomerLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId && c.IsActive)
            .OrderBy(c => c.BuyerName)
            .Select(c => new CustomerReceiptCustomerLookupDto(
                c.Id,
                c.BuyerId,
                c.BuyerName,
                c.OpeningBalance
                    + c.SalesInvoices
                        .Where(si => si.Status == InvoiceStatus.Posted)
                        .Sum(si => si.InvoiceType == InvoiceType.CreditNote ? -si.NetTotal : si.NetTotal)
                    - c.CustomerReceipts
                        .Where(r => r.PaymentMethod != PaymentMethod.Cheque
                                    || (r.Status == CustomerReceiptStatus.Cleared && r.ClearedAt != null))
                        .Sum(r => r.Amount)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerReceiptBankLookupDto>> GetBankLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var banks = await _bankService.GetActiveBankLookupsAsync(cancellationToken);
        return banks
            .Select(b => new CustomerReceiptBankLookupDto(b.Id, b.BankName, b.AccountNumber))
            .ToList();
    }

    public async Task<CustomerReceiptSaveResult> CreateAsync(
        CustomerReceiptSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateSaveRequestAsync(request, null, cancellationToken);
        if (!validation.Success)
        {
            return validation;
        }

        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var now = DateTime.UtcNow;
        var userName = _currentUser.UserName;

        var entity = new CustomerReceipt
        {
            CompanyId = companyId,
            ReceiptNumber = request.ReceiptNumber.Trim(),
            CustomerId = request.CustomerId,
            ReceiptDate = request.ReceiptDate.Date,
            Amount = request.Amount,
            PaymentMethod = request.PaymentMethod,
            ChequeBankType = request.PaymentMethod == PaymentMethod.Cheque ? request.ChequeBankType : null,
            BankId = NormalizeBankId(request),
            ChequeNumber = request.ChequeNumber?.Trim(),
            ChequeDate = request.ChequeDate?.Date,
            Notes = request.Notes?.Trim(),
            CreatedAt = now,
            CreatedBy = userName
        };

        ApplyClearingState(entity, request, now, userName);

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            await _unitOfWork.Repository<CustomerReceipt>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var glResult = await _customerGlPosting.PostCustomerReceiptAsync(entity, cancellationToken);
            if (!glResult.Success)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return new CustomerReceiptSaveResult(false, glResult.Message, null);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Failed to create customer receipt");
            return new CustomerReceiptSaveResult(
                false,
                "Could not save receipt. Check receipt number is unique.",
                null);
        }

        await TryAuditAsync("Create", entity.Id.ToString(), null, JsonSerializer.Serialize(request), cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new CustomerReceiptSaveResult(true, null, dto);
    }

    public async Task<CustomerReceiptSaveResult> UpdateAsync(
        CustomerReceiptSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.Id.HasValue)
        {
            return new CustomerReceiptSaveResult(false, "Receipt id is required.", null);
        }

        var validation = await ValidateSaveRequestAsync(request, request.Id.Value, cancellationToken);
        if (!validation.Success)
        {
            return validation;
        }

        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var entity = await _unitOfWork.Repository<CustomerReceipt>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(r => r.Id == request.Id.Value && r.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new CustomerReceiptSaveResult(false, "Receipt not found.", null);
        }

        if (entity.IsDeposited || CustomerReceiptBalanceRules.IsChequeCleared(entity.Status, entity.ClearedAt))
        {
            return new CustomerReceiptSaveResult(
                false,
                "This cheque has been deposited or cleared and cannot be edited.",
                null);
        }

        var previousAmount = entity.Amount;
        var previousBankId = entity.BankId;
        var previousPaymentMethod = entity.PaymentMethod;
        var previousChequeBankType = entity.ChequeBankType;

        var oldSnapshot = JsonSerializer.Serialize(new
        {
            entity.ReceiptNumber,
            entity.CustomerId,
            entity.ReceiptDate,
            entity.Amount,
            entity.PaymentMethod,
            entity.BankId,
            entity.ChequeNumber,
            entity.ChequeDate,
            entity.ChequeBankType,
            entity.Notes
        });

        entity.ReceiptNumber = request.ReceiptNumber.Trim();
        entity.CustomerId = request.CustomerId;
        entity.ReceiptDate = request.ReceiptDate.Date;
        entity.Amount = request.Amount;
        entity.PaymentMethod = request.PaymentMethod;
        entity.ChequeBankType = request.PaymentMethod == PaymentMethod.Cheque ? request.ChequeBankType : null;
        entity.BankId = NormalizeBankId(request);
        entity.ChequeNumber = request.ChequeNumber?.Trim();
        entity.ChequeDate = request.ChequeDate?.Date;
        entity.Notes = request.Notes?.Trim();
        ApplyClearingState(entity, request, DateTime.UtcNow, _currentUser.UserName);
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = _currentUser.UserName;

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            _unitOfWork.Repository<CustomerReceipt>().Update(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var glResult = await _customerGlPosting.SyncCustomerReceiptAsync(
                entity,
                previousAmount,
                previousBankId,
                previousPaymentMethod,
                previousChequeBankType,
                cancellationToken);

            if (!glResult.Success)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return new CustomerReceiptSaveResult(false, glResult.Message, null);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Failed to update customer receipt {ReceiptId}", request.Id);
            return new CustomerReceiptSaveResult(
                false,
                "Could not update receipt. Check receipt number is unique.",
                null);
        }

        await TryAuditAsync("Update", entity.Id.ToString(), oldSnapshot, JsonSerializer.Serialize(request), cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new CustomerReceiptSaveResult(true, null, dto);
    }

    public async Task<CustomerReceiptSaveResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var entity = await _unitOfWork.Repository<CustomerReceipt>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new CustomerReceiptSaveResult(false, "Receipt not found.", null);
        }

        if (entity.IsDeposited || CustomerReceiptBalanceRules.IsChequeCleared(entity.Status, entity.ClearedAt))
        {
            return new CustomerReceiptSaveResult(
                false,
                "This cheque has been deposited or cleared and cannot be deleted.",
                null);
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        var glResult = await _customerGlPosting.RemoveCustomerReceiptAsync(entity.Id, cancellationToken);
        if (!glResult.Success)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            return new CustomerReceiptSaveResult(false, glResult.Message, null);
        }

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = _currentUser.UserName;

        _unitOfWork.Repository<CustomerReceipt>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await TryAuditAsync("Delete", id.ToString(), JsonSerializer.Serialize(entity), null, cancellationToken);

        return new CustomerReceiptSaveResult(true, "Receipt deleted.", null);
    }

    public async Task<CustomerReceiptSaveResult> ApproveClearanceAsync(
        int id,
        CustomerReceiptApproveClearanceRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var entity = await _unitOfWork.Repository<CustomerReceipt>()
            .Query(asNoTracking: false)
            .Include(r => r.DepositedBankTransaction)
            .FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new CustomerReceiptSaveResult(false, "Receipt not found.", null);
        }

        if (entity.PaymentMethod != PaymentMethod.Cheque)
        {
            return new CustomerReceiptSaveResult(false, "Only cheque receipts can be approved for clearance.", null);
        }

        if (entity.ChequeBankType == ChequeBankType.SameBank)
        {
            return new CustomerReceiptSaveResult(false, "Same-bank cheques are cleared immediately and cannot be approved.", null);
        }

        if (CustomerReceiptBalanceRules.IsChequeCleared(entity.Status, entity.ClearedAt))
        {
            return new CustomerReceiptSaveResult(false, "This cheque has already been cleared.", null);
        }

        var bankChartOfAccountId = request?.BankChartOfAccountId;
        if (!bankChartOfAccountId.HasValue && entity.DepositedBankTransaction is not null)
        {
            bankChartOfAccountId = entity.DepositedBankTransaction.ChartOfAccountId;
        }

        if (!bankChartOfAccountId.HasValue)
        {
            return new CustomerReceiptSaveResult(
                false,
                "Deposit this cheque via Make Deposit first, or select the bank account to credit.",
                null);
        }

        var glRepair = await EnsureChequeGlChainAsync(entity, cancellationToken);
        if (!glRepair.Success)
        {
            return new CustomerReceiptSaveResult(false, glRepair.Message, null);
        }

        var now = DateTime.UtcNow;
        entity.Status = CustomerReceiptStatus.Cleared;
        entity.ClearedAt = now;
        entity.ClearedBy = _currentUser.UserName;

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            _unitOfWork.Repository<CustomerReceipt>().Update(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (!entity.IsDeposited)
            {
                var glResult = await _customerGlPosting.PostChequeClearanceAsync(
                    entity,
                    bankChartOfAccountId.Value,
                    cancellationToken);

                if (!glResult.Success)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return new CustomerReceiptSaveResult(false, glResult.Message, null);
                }
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Failed to approve cheque clearance for receipt {ReceiptId}", id);
            return new CustomerReceiptSaveResult(false, "Could not approve cheque clearance.", null);
        }

        await TryAuditAsync("ApproveClearance", entity.Id.ToString(), null, null, cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        var message = entity.IsDeposited
            ? "Cheque cleared. Customer balance updated (bank was credited on deposit)."
            : "Cheque cleared. Customer balance and bank account updated.";
        return new CustomerReceiptSaveResult(true, message, dto);
    }

    private async Task<CustomerReceiptSaveResult> ValidateSaveRequestAsync(
        CustomerReceiptSaveRequest request,
        int? excludeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ReceiptNumber))
        {
            return new CustomerReceiptSaveResult(false, "Receipt number is required.", null);
        }

        if (request.CustomerId <= 0)
        {
            return new CustomerReceiptSaveResult(false, "Customer is required.", null);
        }

        if (request.Amount <= 0)
        {
            return new CustomerReceiptSaveResult(false, "Amount must be greater than zero.", null);
        }

        if (request.ReceiptDate == default)
        {
            return new CustomerReceiptSaveResult(false, "Receipt date is required.", null);
        }

        if (request.PaymentMethod == PaymentMethod.Cheque)
        {
            if (!request.ChequeBankType.HasValue)
            {
                return new CustomerReceiptSaveResult(false, "Select Same Bank or Other Bank for cheque payments.", null);
            }

            if (request.ChequeBankType == ChequeBankType.SameBank && !request.BankId.HasValue)
            {
                return new CustomerReceiptSaveResult(false, "Bank account is required for same-bank cheques.", null);
            }

            if (string.IsNullOrWhiteSpace(request.ChequeNumber))
            {
                return new CustomerReceiptSaveResult(false, "Cheque number is required.", null);
            }

            if (!request.ChequeDate.HasValue)
            {
                return new CustomerReceiptSaveResult(false, "Cheque date is required.", null);
            }
        }

        if (request.PaymentMethod == PaymentMethod.BankTransfer && !request.BankId.HasValue)
        {
            return new CustomerReceiptSaveResult(false, "Bank account is required for bank transfer payments.", null);
        }

        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        var customerExists = await _unitOfWork.Repository<Customer>()
            .Query()
            .AnyAsync(c => c.Id == request.CustomerId && c.CompanyId == companyId && c.IsActive, cancellationToken);

        if (!customerExists)
        {
            return new CustomerReceiptSaveResult(false, "Selected customer is not valid.", null);
        }

        if ((request.PaymentMethod == PaymentMethod.BankTransfer
             || (request.PaymentMethod == PaymentMethod.Cheque && request.ChequeBankType == ChequeBankType.SameBank))
            && request.BankId.HasValue)
        {
            var bankExists = await _unitOfWork.Repository<Bank>()
                .Query()
                .AnyAsync(b => b.Id == request.BankId && b.CompanyId == companyId && b.IsActive && !b.IsDeleted, cancellationToken);

            if (!bankExists)
            {
                return new CustomerReceiptSaveResult(false, "Selected bank account is not valid.", null);
            }
        }

        var duplicateNumber = await _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .AnyAsync(r =>
                r.CompanyId == companyId
                && r.ReceiptNumber == request.ReceiptNumber.Trim()
                && (!excludeId.HasValue || r.Id != excludeId.Value),
                cancellationToken);

        if (duplicateNumber)
        {
            return new CustomerReceiptSaveResult(false, "Receipt number already exists.", null);
        }

        return new CustomerReceiptSaveResult(true, null, null);
    }

    private bool TryGetCompanyId(out int companyId, out CustomerReceiptSaveResult? error)
    {
        try
        {
            companyId = _currentCompany.GetRequiredCompanyId();
            error = null;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            companyId = 0;
            error = new CustomerReceiptSaveResult(false, ex.Message, null);
            return false;
        }
    }

    private async Task TryAuditAsync(
        string action,
        string entityId,
        string? oldValues,
        string? newValues,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditService.LogAsync(
                ReferenceTypes.CustomerReceipt,
                entityId,
                action,
                oldValues,
                newValues,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for customer receipt {EntityId}", entityId);
        }
    }

    private static int? NormalizeBankId(CustomerReceiptSaveRequest request) =>
        request.PaymentMethod == PaymentMethod.BankTransfer
        || (request.PaymentMethod == PaymentMethod.Cheque && request.ChequeBankType == ChequeBankType.SameBank)
            ? request.BankId
            : null;

    private static void ApplyClearingState(
        CustomerReceipt entity,
        CustomerReceiptSaveRequest request,
        DateTime now,
        string? userName)
    {
        if (request.PaymentMethod == PaymentMethod.Cheque && request.ChequeBankType == ChequeBankType.OtherBank)
        {
            entity.Status = CustomerReceiptStatus.InClearing;
            entity.ClearedAt = null;
            entity.ClearedBy = null;
            return;
        }

        if (request.PaymentMethod == PaymentMethod.Cheque && request.ChequeBankType == ChequeBankType.SameBank)
        {
            entity.Status = CustomerReceiptStatus.Cleared;
            entity.ClearedAt = now;
            entity.ClearedBy = userName;
            return;
        }

        entity.Status = CustomerReceiptStatus.Cleared;
        entity.ClearedAt = null;
        entity.ClearedBy = null;
    }

    private async Task<(bool Success, string? Message)> EnsureChequeGlChainAsync(
        CustomerReceipt entity,
        CancellationToken cancellationToken)
    {
        var companyId = entity.CompanyId;

        if (!await HasPostedReceiptJournalAsync(companyId, entity.Id, cancellationToken))
        {
            var receiveGl = await _customerGlPosting.PostCustomerReceiptAsync(entity, cancellationToken);
            if (!receiveGl.Success)
            {
                return (false, receiveGl.Message);
            }
        }

        if (entity.IsDeposited
            && entity.DepositedBankTransaction is not null
            && entity.DepositedBankTransaction.JournalEntryId is null)
        {
            var depositGl = await _bankGlPosting.PostBankTransactionAsync(
                entity.DepositedBankTransaction,
                cancellationToken);
            if (!depositGl.Success)
            {
                return (false, depositGl.Message);
            }
        }

        return (true, null);
    }

    private async Task<bool> HasPostedReceiptJournalAsync(
        int companyId,
        int receiptId,
        CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<JournalEntry>()
            .Query()
            .AnyAsync(
                j => j.CompanyId == companyId
                     && j.ReferenceType == ReferenceTypes.CustomerReceipt
                     && j.ReferenceId == receiptId
                     && j.Status == JournalStatus.Posted
                     && !j.IsDeleted,
                cancellationToken);

    private static string GetDepositStatusLabel(
        PaymentMethod paymentMethod,
        ChequeBankType? chequeBankType,
        CustomerReceiptStatus status,
        DateTime? clearedAt,
        bool isDeposited,
        DateTime? chequeDate,
        DateTime today)
    {
        if (paymentMethod != PaymentMethod.Cheque)
        {
            return "—";
        }

        if (chequeBankType == ChequeBankType.SameBank)
        {
            return CustomerReceiptBalanceRules.IsChequeCleared(status, clearedAt) ? "Cleared" : "—";
        }

        if (CustomerReceiptBalanceRules.IsChequeCleared(status, clearedAt))
        {
            return "Cleared";
        }

        if (isDeposited)
        {
            return "Deposited (Awaiting Approval)";
        }

        if (chequeDate.HasValue && chequeDate.Value.Date > today)
        {
            return "In Clearing (Post-dated)";
        }

        return "In Clearing";
    }

    private static IQueryable<CustomerReceipt> ApplyOrdering(IQueryable<CustomerReceipt> query, DataTableRequest request)
    {
        var desc = string.Equals(request.OrderDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return request.OrderColumn switch
        {
            0 => desc ? query.OrderByDescending(r => r.ReceiptNumber) : query.OrderBy(r => r.ReceiptNumber),
            1 => desc ? query.OrderByDescending(r => r.Customer.BuyerName) : query.OrderBy(r => r.Customer.BuyerName),
            2 => desc ? query.OrderByDescending(r => r.ReceiptDate) : query.OrderBy(r => r.ReceiptDate),
            3 => desc ? query.OrderByDescending(r => r.Amount) : query.OrderBy(r => r.Amount),
            4 => desc ? query.OrderByDescending(r => r.PaymentMethod) : query.OrderBy(r => r.PaymentMethod),
            5 => desc ? query.OrderByDescending(r => r.ChequeNumber) : query.OrderBy(r => r.ChequeNumber),
            6 => desc ? query.OrderByDescending(r => r.ChequeDate) : query.OrderBy(r => r.ChequeDate),
            7 => desc ? query.OrderByDescending(r => r.IsDeposited) : query.OrderBy(r => r.IsDeposited),
            _ => query.OrderByDescending(r => r.ReceiptDate).ThenByDescending(r => r.Id)
        };
    }

    [GeneratedRegex(@"^RCP-(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ReceiptNumberRegex();
}
