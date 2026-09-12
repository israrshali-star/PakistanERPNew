using Microsoft.EntityFrameworkCore;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Entities;
using PakistanAccountingERP.Domain.Enums;

namespace PakistanAccountingERP.Application.Services;

public class CustomerReceiptInvoiceAllocationService : ICustomerReceiptInvoiceAllocationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentCompanyService _currentCompany;

    public CustomerReceiptInvoiceAllocationService(
        IUnitOfWork unitOfWork,
        ICurrentCompanyService currentCompany)
    {
        _unitOfWork = unitOfWork;
        _currentCompany = currentCompany;
    }

    public async Task<CustomerReceiptInvoiceAllocationDto?> GetAllocationAsync(
        int customerId,
        DateTime receiptDate,
        decimal amount,
        int? receiptId = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = _currentCompany.GetRequiredCompanyId();
        var receiptDateOnly = receiptDate.Date;

        var customer = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => c.Id == customerId && c.CompanyId == companyId)
            .Select(c => new { c.Id, c.OpeningBalance })
            .FirstOrDefaultAsync(cancellationToken);

        if (customer is null)
        {
            return null;
        }

        var invoices = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(si =>
                si.CustomerId == customerId
                && si.CompanyId == companyId
                && si.Status == InvoiceStatus.Posted)
            .Select(si => new
            {
                si.Id,
                si.InvoiceNumber,
                si.InvoiceDate,
                si.InvoiceType,
                si.NetTotal
            })
            .ToListAsync(cancellationToken);

        var receipts = await _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .Where(r => r.CustomerId == customerId && r.CompanyId == companyId)
            .Select(r => new
            {
                r.Id,
                r.ReceiptDate,
                r.Amount,
                r.PaymentMethod,
                r.Status,
                r.ClearedAt
            })
            .ToListAsync(cancellationToken);

        var writeCheques = await _unitOfWork.Repository<BankTransaction>()
            .Query()
            .Where(bt =>
                bt.CustomerId == customerId
                && bt.CompanyId == companyId
                && bt.TransactionType == BankTransactionType.Withdrawal
                && !bt.IsDeleted
                && bt.JournalEntryId != null)
            .Select(bt => new
            {
                bt.Id,
                bt.TransactionDate,
                bt.ChequeNumber,
                bt.CustomerBalanceEffect
            })
            .ToListAsync(cancellationToken);

        var invoiceNet = invoices.Sum(i =>
            i.InvoiceType == InvoiceType.CreditNote ? -i.NetTotal : i.NetTotal);
        var otherReceipts = receipts
            .Where(r => r.Id != receiptId
                        && CustomerReceiptBalanceRules.AffectsCustomerBalance(
                            r.PaymentMethod,
                            r.Status,
                            r.ClearedAt))
            .Sum(r => r.Amount);
        var writeChequeEffect = writeCheques.Sum(bt => bt.CustomerBalanceEffect);
        var outstandingBefore = customer.OpeningBalance + invoiceNet - otherReceipts + writeChequeEffect;

        var movements = new List<CustomerReceiptInvoiceAllocator.Movement>(
            invoices.Count + receipts.Count + writeCheques.Count + 1);

        foreach (var invoice in invoices)
        {
            var isCreditNote = invoice.InvoiceType == InvoiceType.CreditNote;
            movements.Add(new CustomerReceiptInvoiceAllocator.Movement(
                invoice.InvoiceDate.Date,
                invoice.Id,
                IsReceivable: !isCreditNote,
                invoice.InvoiceNumber,
                invoice.NetTotal));
        }

        foreach (var receipt in receipts)
        {
            if (receipt.Id == receiptId)
            {
                continue;
            }

            if (!CustomerReceiptBalanceRules.AffectsCustomerBalance(
                    receipt.PaymentMethod,
                    receipt.Status,
                    receipt.ClearedAt))
            {
                continue;
            }

            movements.Add(new CustomerReceiptInvoiceAllocator.Movement(
                receipt.ReceiptDate.Date,
                CustomerReceiptInvoiceAllocator.ReceiptSortOffset + receipt.Id,
                IsReceivable: false,
                receipt.Id.ToString(),
                receipt.Amount));
        }

        foreach (var cheque in writeCheques)
        {
            if (cheque.CustomerBalanceEffect == 0m)
            {
                continue;
            }

            var reference = !string.IsNullOrWhiteSpace(cheque.ChequeNumber)
                ? cheque.ChequeNumber.Trim()
                : $"PAY-{cheque.Id:D4}";
            movements.Add(new CustomerReceiptInvoiceAllocator.Movement(
                cheque.TransactionDate.Date,
                CustomerReceiptInvoiceAllocator.WriteChequeSortOffset + cheque.Id,
                IsReceivable: cheque.CustomerBalanceEffect > 0m,
                reference,
                Math.Abs(cheque.CustomerBalanceEffect)));
        }

        var targetSortKey = receiptId.HasValue
            ? CustomerReceiptInvoiceAllocator.ReceiptSortOffset + receiptId.Value
            : int.MaxValue;
        movements.Add(new CustomerReceiptInvoiceAllocator.Movement(
            receiptDateOnly,
            targetSortKey,
            IsReceivable: false,
            "Receipt",
            Math.Max(0m, amount),
            IsTargetReceipt: true));

        return CustomerReceiptInvoiceAllocator.Allocate(
            customer.OpeningBalance,
            movements,
            Math.Max(0m, amount),
            outstandingBefore);
    }

    public async Task<IReadOnlyDictionary<int, decimal>> GetRemainingByInvoiceIdAsync(
        IReadOnlyCollection<int> customerIds,
        CancellationToken cancellationToken = default)
    {
        var remaining = new Dictionary<int, decimal>();
        var ids = customerIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            return remaining;
        }

        var companyId = _currentCompany.GetRequiredCompanyId();

        var customers = await _unitOfWork.Repository<Customer>()
            .Query()
            .Where(c => ids.Contains(c.Id) && c.CompanyId == companyId)
            .Select(c => new { c.Id, c.OpeningBalance })
            .ToListAsync(cancellationToken);

        if (customers.Count == 0)
        {
            return remaining;
        }

        var invoices = await _unitOfWork.Repository<SalesInvoice>()
            .Query()
            .Where(si =>
                ids.Contains(si.CustomerId)
                && si.CompanyId == companyId
                && si.Status == InvoiceStatus.Posted)
            .Select(si => new
            {
                si.Id,
                si.CustomerId,
                si.InvoiceNumber,
                si.InvoiceDate,
                si.InvoiceType,
                si.NetTotal
            })
            .ToListAsync(cancellationToken);

        var receipts = await _unitOfWork.Repository<CustomerReceipt>()
            .Query()
            .Where(r => ids.Contains(r.CustomerId) && r.CompanyId == companyId)
            .Select(r => new
            {
                r.Id,
                r.CustomerId,
                r.ReceiptDate,
                r.Amount,
                r.PaymentMethod,
                r.Status,
                r.ClearedAt
            })
            .ToListAsync(cancellationToken);

        var writeCheques = await _unitOfWork.Repository<BankTransaction>()
            .Query()
            .Where(bt =>
                bt.CustomerId != null
                && ids.Contains(bt.CustomerId.Value)
                && bt.CompanyId == companyId
                && bt.TransactionType == BankTransactionType.Withdrawal
                && !bt.IsDeleted
                && bt.JournalEntryId != null)
            .Select(bt => new
            {
                bt.Id,
                bt.CustomerId,
                bt.TransactionDate,
                bt.ChequeNumber,
                bt.CustomerBalanceEffect
            })
            .ToListAsync(cancellationToken);

        foreach (var customer in customers)
        {
            var customerInvoices = invoices.Where(i => i.CustomerId == customer.Id).ToList();
            var customerReceipts = receipts.Where(r => r.CustomerId == customer.Id).ToList();
            var customerCheques = writeCheques.Where(bt => bt.CustomerId == customer.Id).ToList();

            var invoiceDebit = 0m;
            var invoiceCredit = 0m;
            foreach (var invoice in customerInvoices)
            {
                if (invoice.InvoiceType == InvoiceType.CreditNote || invoice.NetTotal < 0m)
                {
                    invoiceCredit += Math.Abs(invoice.NetTotal);
                }
                else
                {
                    invoiceDebit += invoice.NetTotal;
                }
            }

            var receiptCredit = customerReceipts
                .Where(r => CustomerReceiptBalanceRules.AffectsCustomerBalance(
                    r.PaymentMethod,
                    r.Status,
                    r.ClearedAt))
                .Sum(r => r.Amount);

            var chequeDebit = customerCheques.Where(bt => bt.CustomerBalanceEffect > 0m)
                .Sum(bt => bt.CustomerBalanceEffect);
            var chequeCredit = customerCheques.Where(bt => bt.CustomerBalanceEffect < 0m)
                .Sum(bt => Math.Abs(bt.CustomerBalanceEffect));

            var partyDebit = Math.Max(0m, customer.OpeningBalance) + invoiceDebit + chequeDebit;
            var partyCredit = Math.Max(0m, -customer.OpeningBalance) + invoiceCredit + receiptCredit + chequeCredit;

            // Ledger credit/nil (ignoring bill paisa) means the party has no outstanding.
            if (partyDebit - partyCredit < TradeInvoiceLayout.SalesListPaymentWholeRupee)
            {
                continue;
            }

            var movements = new List<CustomerReceiptInvoiceAllocator.Movement>(
                customerInvoices.Count + customerReceipts.Count + customerCheques.Count);

            foreach (var invoice in customerInvoices)
            {
                var isCredit = invoice.InvoiceType == InvoiceType.CreditNote || invoice.NetTotal < 0m;
                movements.Add(new CustomerReceiptInvoiceAllocator.Movement(
                    invoice.InvoiceDate.Date,
                    invoice.Id,
                    IsReceivable: !isCredit,
                    invoice.InvoiceNumber,
                    Math.Abs(invoice.NetTotal),
                    InvoiceId: isCredit ? null : invoice.Id));
            }

            foreach (var receipt in customerReceipts)
            {
                if (!CustomerReceiptBalanceRules.AffectsCustomerBalance(
                        receipt.PaymentMethod,
                        receipt.Status,
                        receipt.ClearedAt))
                {
                    continue;
                }

                movements.Add(new CustomerReceiptInvoiceAllocator.Movement(
                    receipt.ReceiptDate.Date,
                    CustomerReceiptInvoiceAllocator.ReceiptSortOffset + receipt.Id,
                    IsReceivable: false,
                    receipt.Id.ToString(),
                    receipt.Amount));
            }

            foreach (var cheque in customerCheques)
            {
                if (cheque.CustomerBalanceEffect == 0m)
                {
                    continue;
                }

                var reference = !string.IsNullOrWhiteSpace(cheque.ChequeNumber)
                    ? cheque.ChequeNumber.Trim()
                    : $"PAY-{cheque.Id:D4}";
                movements.Add(new CustomerReceiptInvoiceAllocator.Movement(
                    cheque.TransactionDate.Date,
                    CustomerReceiptInvoiceAllocator.WriteChequeSortOffset + cheque.Id,
                    IsReceivable: cheque.CustomerBalanceEffect > 0m,
                    reference,
                    Math.Abs(cheque.CustomerBalanceEffect)));
            }

            var unpaid = CustomerReceiptInvoiceAllocator.ComputeRemainingByInvoiceId(
                customer.OpeningBalance,
                movements);
            foreach (var pair in unpaid)
            {
                remaining[pair.Key] = pair.Value;
            }
        }

        return remaining;
    }
}
