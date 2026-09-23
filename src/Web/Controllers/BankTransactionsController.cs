using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.Common;
using PakistanAccountingERP.Application.DTOs;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Enums;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[RequirePermission("Banking.View")]
public class BankTransactionsController : Controller
{
    public IActionResult Index() => RedirectToAction(nameof(WriteCheque));

    public IActionResult WriteCheque()
    {
        ViewData["BreadcrumbParent"] = "Banking";
        ViewData["Title"] = "Write Cheque";
        ViewData["TransactionType"] = BankTransactionType.Withdrawal;
        return View("BankOperation");
    }

    public IActionResult MakeDeposit()
    {
        ViewData["BreadcrumbParent"] = "Banking";
        ViewData["Title"] = "Make Deposit";
        ViewData["TransactionType"] = BankTransactionType.Deposit;
        return View("BankOperation");
    }

    public IActionResult Transfer()
    {
        ViewData["BreadcrumbParent"] = "Banking";
        ViewData["Title"] = "Transfer";
        ViewData["TransactionType"] = BankTransactionType.Transfer;
        return View("BankOperation");
    }
}

[Authorize]
[ApiController]
[Route("api/bank-transactions")]
public class BankTransactionsApiController : ControllerBase
{
    private readonly IBankTransactionService _bankTransactionService;
    private readonly IChequePrintPdfService _chequePrintPdfService;

    public BankTransactionsApiController(
        IBankTransactionService bankTransactionService,
        IChequePrintPdfService chequePrintPdfService)
    {
        _bankTransactionService = bankTransactionService;
        _chequePrintPdfService = chequePrintPdfService;
    }

    [HttpGet("datatable")]
    [RequirePermission("Banking.View")]
    public async Task<IActionResult> DataTable(CancellationToken cancellationToken)
    {
        try
        {
            int? bankId = int.TryParse(Request.Query["bankId"], out var bid) ? bid : null;
            BankTransactionType? transactionType = int.TryParse(Request.Query["transactionType"], out var tt)
                ? (BankTransactionType)tt
                : null;

            var request = new DataTableRequest(
                Draw: int.TryParse(Request.Query["draw"], out var draw) ? draw : 0,
                Start: int.TryParse(Request.Query["start"], out var start) ? start : 0,
                Length: int.TryParse(Request.Query["length"], out var length) ? length : 10,
                SearchValue: Request.Query["search[value]"],
                OrderColumn: int.TryParse(Request.Query["order[0][column]"], out var col) ? col : 1,
                OrderDirection: Request.Query["order[0][dir]"].ToString());

            var result = await _bankTransactionService.GetDataTableAsync(
                request,
                bankId,
                transactionType,
                cancellationToken);
            return Ok(new
            {
                draw = result.Draw,
                recordsTotal = result.RecordsTotal,
                recordsFiltered = result.RecordsFiltered,
                data = result.Data
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("coa-banks")]
    [RequirePermission("Banking.View")]
    public async Task<IActionResult> CoaBanks(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _bankTransactionService.GetBankCoaLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("coa-transfer")]
    [RequirePermission("Banking.View")]
    public async Task<IActionResult> CoaTransfer(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _bankTransactionService.GetTransferCoaLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("coa-deposit")]
    [RequirePermission("Banking.View")]
    public async Task<IActionResult> CoaDeposit(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _bankTransactionService.GetDepositCoaLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("coa-counter")]
    [RequirePermission("Banking.View")]
    public async Task<IActionResult> CoaCounter(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _bankTransactionService.GetWriteChequePartyLookupsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("undeposited-summary")]
    [RequirePermission("Banking.View")]
    public async Task<IActionResult> UndepositedSummary(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _bankTransactionService.GetUndepositedSummaryAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("undeposited-cheques")]
    [RequirePermission("Banking.View")]
    public async Task<IActionResult> UndepositedCheques(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _bankTransactionService.GetUndepositedChequesAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("next-cheque-number")]
    [RequirePermission("Banking.View")]
    public async Task<IActionResult> NextChequeNumber(
        [FromQuery] int chartOfAccountId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _bankTransactionService.GetNextChequeNumberAsync(chartOfAccountId, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("next-cheque-number")]
    [RequirePermission("Banking.Create")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SetNextChequeNumber(
        [FromBody] BankNextChequeNumberSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new BankNextChequeNumberSaveResult(false, "Invalid request body.", null));
        }

        try
        {
            var result = await _bankTransactionService.SetNextChequeNumberAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new BankNextChequeNumberSaveResult(false, ex.Message, null));
        }
    }

    [HttpPost]
    [RequirePermission("Banking.Create")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] BankTransactionSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new BankTransactionSaveResult(false, "Invalid request body.", null));
        }

        try
        {
            var result = await _bankTransactionService.CreateAsync(request, cancellationToken);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new BankTransactionSaveResult(false, ex.Message, null));
        }
    }

    [HttpGet("{id:int}/cheque-pdf")]
    [RequirePermission("Banking.View")]
    public async Task<IActionResult> ChequePdf(
        int id,
        [FromQuery] string? payee,
        [FromQuery] string? orientation,
        CancellationToken cancellationToken)
    {
        try
        {
            var transaction = await _bankTransactionService.GetByIdAsync(id, cancellationToken);
            if (transaction is null)
            {
                return NotFound();
            }

            if (transaction.TransactionType != BankTransactionType.Withdrawal
                || transaction.PaymentMethod is not (PaymentMethod.Cheque or PaymentMethod.CashWithdrawal))
            {
                return BadRequest(new { message = "Only posted cheques can be printed." });
            }

            var useCash = string.Equals(payee, "cash", StringComparison.OrdinalIgnoreCase);
            var payeeName = useCash
                ? "Cash"
                : (string.IsNullOrWhiteSpace(transaction.PartyName) ? "Cash" : transaction.PartyName.Trim());

            var chequeDate = transaction.ChequeDate ?? transaction.TransactionDate;
            var pageOrientation = string.Equals(orientation, "portrait", StringComparison.OrdinalIgnoreCase)
                ? ChequePageOrientation.Portrait
                : ChequePageOrientation.Landscape;
            var pdf = _chequePrintPdfService.GeneratePdf(new ChequePrintDto(
                payeeName,
                chequeDate,
                transaction.Amount,
                AmountInWords.ToChequeLeafWords(transaction.Amount),
                AmountInWords.ToChequeFigures(transaction.Amount),
                transaction.ChequeNumber,
                pageOrientation));

            var safeNumber = string.IsNullOrWhiteSpace(transaction.ChequeNumber)
                ? id.ToString()
                : transaction.ChequeNumber.Replace(" ", string.Empty);
            return File(pdf, "application/pdf", $"cheque-{safeNumber}.pdf");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
