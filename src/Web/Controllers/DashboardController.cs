using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Web.Authorization;

namespace PakistanAccountingERP.Web.Controllers;

[Authorize]
[Route("api/[controller]")]
public class DashboardController : Controller
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet]
    [RequirePermission("Dashboard.View")]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var data = await _dashboardService.GetDashboardDataAsync(cancellationToken);
        return Ok(data);
    }

    [HttpGet("summary")]
    [RequirePermission("Dashboard.View")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        return Ok(await _dashboardService.GetSummaryAsync(cancellationToken));
    }

    [HttpGet("daily-sales")]
    [RequirePermission("Dashboard.View")]
    public async Task<IActionResult> DailySales(CancellationToken cancellationToken)
    {
        return Ok(await _dashboardService.GetDailySalesAsync(cancellationToken));
    }

    [HttpGet("monthly-profit-loss")]
    [RequirePermission("Dashboard.View")]
    public async Task<IActionResult> MonthlyProfitLoss(CancellationToken cancellationToken)
    {
        return Ok(await _dashboardService.GetMonthlyProfitLossAsync(cancellationToken));
    }

    [HttpGet("monthly-sales")]
    [RequirePermission("Dashboard.View")]
    public async Task<IActionResult> MonthlySales(CancellationToken cancellationToken)
    {
        return Ok(await _dashboardService.GetMonthlySalesAsync(cancellationToken));
    }

    [HttpGet("top-customers")]
    [RequirePermission("Dashboard.View")]
    public async Task<IActionResult> TopCustomers(CancellationToken cancellationToken)
    {
        return Ok(await _dashboardService.GetTopCustomersByBalanceAsync(cancellationToken: cancellationToken));
    }

    [HttpGet("low-stock")]
    [RequirePermission("Dashboard.View")]
    public async Task<IActionResult> LowStock(CancellationToken cancellationToken)
    {
        return Ok(await _dashboardService.GetLowStockItemsAsync(cancellationToken));
    }

    [HttpGet("recent-invoices")]
    [RequirePermission("Dashboard.View")]
    public async Task<IActionResult> RecentInvoices(CancellationToken cancellationToken)
    {
        return Ok(await _dashboardService.GetRecentInvoicesAsync(cancellationToken: cancellationToken));
    }
}
