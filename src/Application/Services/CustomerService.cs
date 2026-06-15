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

public partial class CustomerService : ICustomerService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly ICustomerGlPostingService _customerGlPosting;
    private readonly ILogger<CustomerService> _logger;

    public CustomerService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany,
        ICurrentUserService currentUser,
        IAuditService auditService,
        ICustomerGlPostingService customerGlPosting,
        ILogger<CustomerService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _auditService = auditService;
        _customerGlPosting = customerGlPosting;
        _logger = logger;
    }

    public async Task<DataTableResponse<CustomerListItemDto>> GetDataTableAsync(
        DataTableRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        var query = _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId);

        var recordsTotal = await query.CountAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var term = request.SearchValue.Trim();
            query = query.Where(c =>
                c.BuyerId.Contains(term)
                || c.BuyerName.Contains(term)
                || (c.NTN != null && c.NTN.Contains(term))
                || (c.Email != null && c.Email.Contains(term))
                || (c.Phone != null && c.Phone.Contains(term))
                || (c.Mobile != null && c.Mobile.Contains(term)));
        }

        var recordsFiltered = await query.CountAsync(cancellationToken);

        query = ApplyOrdering(query, request);

        if (request.Length > 0)
        {
            query = query.Skip(request.Start).Take(request.Length);
        }

        var rows = await query
            .Select(c => new CustomerListItemDto(
                c.Id,
                c.BuyerId,
                c.BuyerName,
                c.CustomerType.ToString(),
                c.Province != null ? c.Province.Name : null,
                c.NTN,
                c.Phone ?? c.Mobile,
                c.OpeningBalance,
                c.OpeningBalance
                    + c.SalesInvoices
                        .Where(si => si.Status == InvoiceStatus.Posted)
                        .Sum(si => si.InvoiceType == InvoiceType.CreditNote ? -si.NetTotal : si.NetTotal)
                    - c.CustomerReceipts
                        .Where(r => r.PaymentMethod != PaymentMethod.Cheque
                                    || (r.Status == CustomerReceiptStatus.Cleared && r.ClearedAt != null))
                        .Sum(r => r.Amount)
                    + c.WriteChequePayments
                        .Where(bt => bt.TransactionType == BankTransactionType.Withdrawal && !bt.IsDeleted)
                        .Sum(bt => bt.CustomerBalanceEffect),
                c.IsActive))
            .ToListAsync(cancellationToken);

        return new DataTableResponse<CustomerListItemDto>(
            request.Draw,
            recordsTotal,
            recordsFiltered,
            rows);
    }

    public async Task<CustomerDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();

        return await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.Id == id && c.CompanyId == companyId)
            .Select(c => new CustomerDto(
                c.Id,
                c.BuyerId,
                c.BuyerName,
                c.OpeningBalance,
                c.OpeningBalance
                    + c.SalesInvoices
                        .Where(si => si.Status == InvoiceStatus.Posted)
                        .Sum(si => si.InvoiceType == InvoiceType.CreditNote ? -si.NetTotal : si.NetTotal)
                    - c.CustomerReceipts
                        .Where(r => r.PaymentMethod != PaymentMethod.Cheque
                                    || (r.Status == CustomerReceiptStatus.Cleared && r.ClearedAt != null))
                        .Sum(r => r.Amount)
                    + c.WriteChequePayments
                        .Where(bt => bt.TransactionType == BankTransactionType.Withdrawal && !bt.IsDeleted)
                        .Sum(bt => bt.CustomerBalanceEffect),
                c.Address,
                c.ProvinceId,
                c.Province != null ? c.Province.Name : null,
                c.ScenarioId,
                c.ScenarioType.Code,
                c.Phone,
                c.Mobile,
                c.Email,
                c.NTN,
                c.CNIC,
                c.STRN,
                c.CustomerType,
                c.InvoiceType,
                c.IsActive,
                c.SalesInvoices.Any()))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<NextBuyerIdDto> GenerateNextBuyerIdAsync(CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var prefix = AppConstants.CustomerIdPrefix;

        var buyerIds = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.CompanyId == companyId && c.BuyerId.StartsWith(prefix))
            .Select(c => c.BuyerId)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var buyerId in buyerIds)
        {
            var match = BuyerIdRegex().Match(buyerId);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var number))
            {
                max = Math.Max(max, number);
            }
        }

        return new NextBuyerIdDto($"{prefix}{(max + 1):D4}");
    }

    public async Task<CustomerSaveResult> CreateAsync(
        CustomerSaveRequest request,
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

        var entity = new Customer
        {
            CompanyId = companyId,
            BuyerId = request.BuyerId.Trim(),
            BuyerName = request.BuyerName.Trim(),
            OpeningBalance = request.OpeningBalance,
            Address = CustomerAddressHelper.RemoveLeadingBuyerName(request.BuyerName.Trim(), request.Address?.Trim()),
            ProvinceId = request.ProvinceId,
            ScenarioId = request.ScenarioId,
            Phone = request.Phone?.Trim(),
            Mobile = request.Mobile?.Trim(),
            Email = request.Email?.Trim(),
            NTN = request.NTN?.Trim(),
            CNIC = request.CNIC?.Trim(),
            STRN = request.STRN?.Trim(),
            CustomerType = request.CustomerType,
            InvoiceType = request.InvoiceType,
            IsActive = request.IsActive,
            CreatedAt = now,
            CreatedBy = _currentUser.UserName ?? "system"
        };

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            await _unitOfWork.Repository<Customer>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var glResult = await _customerGlPosting.SyncCustomerOpeningBalanceAsync(
                entity.Id,
                entity.BuyerName,
                entity.OpeningBalance,
                cancellationToken: cancellationToken);

            if (!glResult.Success)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return new CustomerSaveResult(false, glResult.Message, null);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Failed to create customer {BuyerId}", request.BuyerId);
            return new CustomerSaveResult(
                false,
                "Could not save customer. Check FBR scenario, province, and company selection.",
                null);
        }

        await TryAuditAsync("Create", entity.Id.ToString(), null, JsonSerializer.Serialize(request), cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new CustomerSaveResult(true, null, dto);
    }

    public async Task<CustomerSaveResult> UpdateAsync(
        CustomerSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.Id.HasValue)
        {
            return new CustomerSaveResult(false, "Customer id is required.", null);
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

        var entity = await _unitOfWork.Repository<Customer>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(c => c.Id == request.Id.Value && c.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new CustomerSaveResult(false, "Customer not found.", null);
        }

        var previousOpeningBalance = entity.OpeningBalance;

        var oldSnapshot = JsonSerializer.Serialize(new
        {
            entity.BuyerId,
            entity.BuyerName,
            entity.OpeningBalance,
            entity.ProvinceId,
            entity.ScenarioId,
            entity.IsActive
        });

        entity.BuyerId = request.BuyerId.Trim();
        entity.BuyerName = request.BuyerName.Trim();
        entity.OpeningBalance = request.OpeningBalance;
        entity.Address = CustomerAddressHelper.RemoveLeadingBuyerName(request.BuyerName.Trim(), request.Address?.Trim());
        entity.ProvinceId = request.ProvinceId;
        entity.ScenarioId = request.ScenarioId;
        entity.Phone = request.Phone?.Trim();
        entity.Mobile = request.Mobile?.Trim();
        entity.Email = request.Email?.Trim();
        entity.NTN = request.NTN?.Trim();
        entity.CNIC = request.CNIC?.Trim();
        entity.STRN = request.STRN?.Trim();
        entity.CustomerType = request.CustomerType;
        entity.InvoiceType = request.InvoiceType;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = _currentUser.UserName;

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            _unitOfWork.Repository<Customer>().Update(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (previousOpeningBalance != entity.OpeningBalance)
            {
                var glResult = await _customerGlPosting.SyncCustomerOpeningBalanceAsync(
                    entity.Id,
                    entity.BuyerName,
                    entity.OpeningBalance,
                    cancellationToken: cancellationToken);

                if (!glResult.Success)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return new CustomerSaveResult(false, glResult.Message, null);
                }
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Failed to update customer {CustomerId}", request.Id);
            return new CustomerSaveResult(
                false,
                "Could not update customer. Check FBR scenario and province.",
                null);
        }

        await TryAuditAsync("Update", entity.Id.ToString(), oldSnapshot, JsonSerializer.Serialize(request), cancellationToken);

        var dto = await GetByIdAsync(entity.Id, cancellationToken);
        return new CustomerSaveResult(true, null, dto);
    }

    public async Task<CustomerSaveResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var entity = await _unitOfWork.Repository<Customer>()
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId, cancellationToken);

        if (entity is null)
        {
            return new CustomerSaveResult(false, "Customer not found.", null);
        }

        var hasInvoices = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .AnyAsync(si => si.CustomerId == id, cancellationToken);

        if (hasInvoices)
        {
            return new CustomerSaveResult(
                false,
                "Cannot delete this customer because sales invoices exist.",
                null);
        }

        var oldSnapshot = JsonSerializer.Serialize(new { entity.BuyerId, entity.BuyerName });

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            await _customerGlPosting.RemoveCustomerOpeningBalanceAsync(entity.Id, cancellationToken);
            _unitOfWork.Repository<Customer>().Remove(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Failed to delete customer {CustomerId}", id);
            return new CustomerSaveResult(false, "Could not delete customer.", null);
        }

        await _auditService.LogAsync("Delete", "Customers", id.ToString(), oldSnapshot, null, cancellationToken);
        return new CustomerSaveResult(true, "Customer deleted successfully.", null);
    }

    public async Task<CustomerLedgerDto?> GetLedgerAsync(int id, CancellationToken cancellationToken = default)
    {
        var customer = await GetByIdAsync(id, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var entries = await BuildLedgerEntriesAsync(id, null, null, cancellationToken);
        var closing = entries.Count > 0 ? entries[^1].Balance : customer.OpeningBalance;

        return new CustomerLedgerDto(customer, entries, closing);
    }

    public async Task<CustomerStatementDto?> GetStatementAsync(
        int id,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        var customer = await GetByIdAsync(id, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var from = fromDate.Date;
        var to = toDate.Date.AddDays(1).AddTicks(-1);

        var opening = await GetBalanceBeforeDateAsync(id, from, cancellationToken);
        var entries = await BuildLedgerEntriesAsync(id, from, to, cancellationToken, opening);
        var closing = entries.Count > 0 ? entries[^1].Balance : opening;

        return new CustomerStatementDto(customer, from, toDate.Date, opening, entries, closing);
    }

    private async Task<IReadOnlyList<CustomerLedgerEntryDto>> BuildLedgerEntriesAsync(
        int customerId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken,
        decimal? startingBalance = null)
    {
        var customer = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.Id == customerId)
            .Select(c => new { c.OpeningBalance })
            .FirstAsync(cancellationToken);

        var balance = startingBalance ?? customer.OpeningBalance;
        var entries = new List<CustomerLedgerEntryDto>();

        if (!fromDate.HasValue && customer.OpeningBalance != 0m)
        {
            entries.Add(new CustomerLedgerEntryDto(
                DateTime.MinValue,
                "OPENING",
                "Opening Balance",
                customer.OpeningBalance > 0 ? customer.OpeningBalance : 0m,
                customer.OpeningBalance < 0 ? Math.Abs(customer.OpeningBalance) : 0m,
                customer.OpeningBalance));
            balance = customer.OpeningBalance;
        }
        else if (fromDate.HasValue)
        {
            balance = startingBalance ?? await GetBalanceBeforeDateAsync(customerId, fromDate.Value, cancellationToken);
            if (balance != 0m)
            {
                entries.Add(new CustomerLedgerEntryDto(
                    fromDate.Value.AddDays(-1),
                    "B/F",
                    "Balance Brought Forward",
                    balance > 0 ? balance : 0m,
                    balance < 0 ? Math.Abs(balance) : 0m,
                    balance));
            }
        }

        var invoiceQuery = _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(si => si.CustomerId == customerId && si.Status == InvoiceStatus.Posted);

        if (fromDate.HasValue)
        {
            invoiceQuery = invoiceQuery.Where(si => si.InvoiceDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            invoiceQuery = invoiceQuery.Where(si => si.InvoiceDate <= toDate.Value);
        }

        var invoices = await invoiceQuery
            .OrderBy(si => si.InvoiceDate)
            .ThenBy(si => si.Id)
            .Select(si => new
            {
                si.Id,
                si.InvoiceDate,
                si.InvoiceNumber,
                si.InvoiceType,
                si.NetTotal
            })
            .ToListAsync(cancellationToken);

        var receiptQuery = _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .Where(r => r.CustomerId == customerId);

        if (fromDate.HasValue)
        {
            receiptQuery = receiptQuery.Where(r => r.ReceiptDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            receiptQuery = receiptQuery.Where(r => r.ReceiptDate <= toDate.Value);
        }

        var receipts = await receiptQuery
            .OrderBy(r => r.ReceiptDate)
            .ThenBy(r => r.Id)
            .Select(r => new
            {
                r.ReceiptDate,
                r.ReceiptNumber,
                r.PaymentMethod,
                r.Status,
                r.ClearedAt,
                r.ReturnedAt,
                r.ChequeNumber,
                r.Amount,
                r.Id
            })
            .ToListAsync(cancellationToken);

        var movements = new List<(DateTime Date, int SortKey, string Reference, string Description, decimal Debit, decimal Credit, decimal PendingCredit)>();

        foreach (var invoice in invoices)
        {
            var debit = invoice.NetTotal;
            var credit = 0m;

            if (invoice.InvoiceType == InvoiceType.CreditNote)
            {
                debit = 0m;
                credit = invoice.NetTotal;
            }

            movements.Add((
                invoice.InvoiceDate,
                invoice.Id,
                invoice.InvoiceNumber,
                invoice.InvoiceType.ToString(),
                debit,
                credit,
                0m));
        }

        foreach (var receipt in receipts)
        {
            if (CustomerReceiptBalanceRules.IsChequeReturned(receipt.Status))
            {
                var returnedRef = !string.IsNullOrWhiteSpace(receipt.ChequeNumber)
                    ? receipt.ChequeNumber.Trim()
                    : receipt.ReceiptNumber;
                movements.Add((
                    receipt.ReturnedAt ?? receipt.ReceiptDate,
                    1_000_000 + receipt.Id,
                    receipt.ReceiptNumber,
                    $"Cheque Returned ({returnedRef})",
                    receipt.Amount,
                    0m,
                    0m));
                continue;
            }

            var isPendingCheque = receipt.PaymentMethod == PaymentMethod.Cheque
                                  && !CustomerReceiptBalanceRules.IsChequeCleared(
                                      receipt.Status,
                                      receipt.ClearedAt);
            var chequeRef = !string.IsNullOrWhiteSpace(receipt.ChequeNumber)
                ? receipt.ChequeNumber.Trim()
                : receipt.ReceiptNumber;
            var description = isPendingCheque
                ? $"Cheque in Clearing ({chequeRef})"
                : $"Customer Receipt ({receipt.PaymentMethod})";

            movements.Add((
                receipt.ReceiptDate,
                1_000_000 + receipt.Id,
                receipt.ReceiptNumber,
                description,
                0m,
                isPendingCheque ? 0m : receipt.Amount,
                isPendingCheque ? receipt.Amount : 0m));
        }

        var chequeQuery = _unitOfWork.Repository<BankTransaction>()
            .Query()
            .Where(bt =>
                bt.CustomerId == customerId
                && bt.TransactionType == BankTransactionType.Withdrawal
                && !bt.IsDeleted
                && bt.JournalEntryId != null);

        if (fromDate.HasValue)
        {
            chequeQuery = chequeQuery.Where(bt => bt.TransactionDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            chequeQuery = chequeQuery.Where(bt => bt.TransactionDate <= toDate.Value);
        }

        var cheques = await chequeQuery
            .OrderBy(bt => bt.TransactionDate)
            .ThenBy(bt => bt.Id)
            .Select(bt => new
            {
                bt.Id,
                bt.TransactionDate,
                bt.ChequeNumber,
                bt.PaymentMethod,
                bt.CustomerBalanceEffect
            })
            .ToListAsync(cancellationToken);

        foreach (var cheque in cheques)
        {
            var reference = cheque.PaymentMethod == PaymentMethod.Cheque
                            && !string.IsNullOrWhiteSpace(cheque.ChequeNumber)
                ? cheque.ChequeNumber.Trim()
                : $"PAY-{cheque.Id:D4}";

            var description = cheque.PaymentMethod switch
            {
                PaymentMethod.Cheque => "Cheque Payment",
                PaymentMethod.BankTransfer => "Bank Transfer",
                PaymentMethod.Cash => "Cash Payment",
                _ => "Payment"
            };

            movements.Add((
                cheque.TransactionDate,
                2_000_000 + cheque.Id,
                reference,
                description,
                cheque.CustomerBalanceEffect > 0m ? cheque.CustomerBalanceEffect : 0m,
                cheque.CustomerBalanceEffect < 0m ? Math.Abs(cheque.CustomerBalanceEffect) : 0m,
                0m));
        }

        foreach (var movement in movements.OrderBy(m => m.Date).ThenBy(m => m.SortKey))
        {
            balance += movement.Debit - movement.Credit;

            entries.Add(new CustomerLedgerEntryDto(
                movement.Date,
                movement.Reference,
                movement.Description,
                movement.Debit,
                movement.Credit,
                balance,
                movement.PendingCredit));
        }

        return entries;
    }

    private async Task<decimal> GetBalanceBeforeDateAsync(
        int customerId,
        DateTime beforeDate,
        CancellationToken cancellationToken)
    {
        var customer = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.Id == customerId)
            .Select(c => c.OpeningBalance)
            .FirstAsync(cancellationToken);

        var invoiceNet = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(si => si.CustomerId == customerId
                         && si.Status == InvoiceStatus.Posted
                         && si.InvoiceDate < beforeDate)
            .Select(si => new { si.InvoiceType, si.NetTotal })
            .ToListAsync(cancellationToken);

        var movement = invoiceNet.Sum(i =>
            i.InvoiceType == InvoiceType.CreditNote ? -i.NetTotal : i.NetTotal);

        var receiptTotal = await _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .Where(r =>
                r.CustomerId == customerId
                && r.ReceiptDate < beforeDate
                && (r.PaymentMethod != PaymentMethod.Cheque
                    || (r.Status == CustomerReceiptStatus.Cleared && r.ClearedAt != null)))
            .SumAsync(r => r.Amount, cancellationToken);

        var chequeTotal = await _unitOfWork.Repository<BankTransaction>()
            .Query()
            .Where(bt =>
                bt.CustomerId == customerId
                && bt.TransactionType == BankTransactionType.Withdrawal
                && !bt.IsDeleted
                && bt.JournalEntryId != null
                && bt.TransactionDate < beforeDate)
            .SumAsync(bt => bt.CustomerBalanceEffect, cancellationToken);

        return customer + movement - receiptTotal + chequeTotal;
    }

    private async Task<CustomerSaveResult> ValidateSaveRequestAsync(
        CustomerSaveRequest request,
        int? existingId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return new CustomerSaveResult(false, "Buyer ID is required.", null);
        }

        if (string.IsNullOrWhiteSpace(request.BuyerName))
        {
            return new CustomerSaveResult(false, "Buyer name is required.", null);
        }

        if (!TryGetCompanyId(out var companyId, out var companyError))
        {
            return companyError!;
        }

        if (request.ScenarioId <= 0)
        {
            return new CustomerSaveResult(false, "FBR scenario is required.", null);
        }

        var buyerIdExists = await _unitOfWork.Repository<Customer>()
            .Query()
            .AnyAsync(
                c => c.CompanyId == companyId
                     && c.BuyerId == request.BuyerId.Trim()
                     && (!existingId.HasValue || c.Id != existingId.Value),
                cancellationToken);

        if (buyerIdExists)
        {
            return new CustomerSaveResult(false, "Buyer ID already exists for this company.", null);
        }

        var scenarioExists = await _unitOfWork.Repository<ScenarioType>()
            .Query()
            .AnyAsync(s => s.ScenarioId == request.ScenarioId, cancellationToken);

        if (!scenarioExists)
        {
            return new CustomerSaveResult(false, "Invalid FBR scenario type.", null);
        }

        return new CustomerSaveResult(true, null, null);
    }

    private static IQueryable<Customer> ApplyOrdering(IQueryable<Customer> query, DataTableRequest request)
    {
        var desc = string.Equals(request.OrderDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return request.OrderColumn switch
        {
            0 => desc ? query.OrderByDescending(c => c.BuyerId) : query.OrderBy(c => c.BuyerId),
            1 => desc ? query.OrderByDescending(c => c.BuyerName) : query.OrderBy(c => c.BuyerName),
            2 => desc ? query.OrderByDescending(c => c.CustomerType) : query.OrderBy(c => c.CustomerType),
            5 => desc ? query.OrderByDescending(c => c.OpeningBalance) : query.OrderBy(c => c.OpeningBalance),
            _ => desc ? query.OrderByDescending(c => c.BuyerName) : query.OrderBy(c => c.BuyerName)
        };
    }

    private bool TryGetCompanyId(out int companyId, out CustomerSaveResult? error)
    {
        if (!_currentCompany.CompanyId.HasValue)
        {
            companyId = 0;
            error = new CustomerSaveResult(
                false,
                "No company is selected. Please choose a company from the top navbar.",
                null);
            return false;
        }

        companyId = _currentCompany.CompanyId.Value;
        error = null;
        return true;
    }

    private async Task TryAuditAsync(
        string action,
        string recordId,
        string? oldValue,
        string? newValue,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditService.LogAsync(action, "Customers", recordId, oldValue, newValue, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log failed for customer {RecordId}", recordId);
        }
    }

    [GeneratedRegex(@"^CUST-(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex BuyerIdRegex();
}
