(function () {
    'use strict';

    let salesChart = null;
    let dailySalesChart = null;
    let profitLossChart = null;

    function formatDate(value) {
        const d = new Date(value);
        if (Number.isNaN(d.getTime())) {
            return value;
        }
        const day = String(d.getDate()).padStart(2, '0');
        const month = String(d.getMonth() + 1).padStart(2, '0');
        const year = d.getFullYear();
        return day + '/' + month + '/' + year;
    }

    function formatCompactCurrency(value) {
        const num = parseFloat(value) || 0;
        if (Math.abs(num) >= 1000000) {
            return (num / 1000000).toFixed(1) + 'M';
        }
        if (Math.abs(num) >= 1000) {
            return (num / 1000).toFixed(0) + 'K';
        }
        return num.toLocaleString('en-PK', { maximumFractionDigits: 0 });
    }

    function setKpis(summary) {
        $('[data-kpi="todaySales"]').text(formatCurrency(summary.todaySales));
        $('[data-kpi="monthSales"]').text(formatCurrency(summary.monthSales));
        $('[data-kpi="outstandingReceivables"]').text(formatCurrency(summary.outstandingReceivables));
        $('[data-kpi="outstandingPayables"]').text(formatCurrency(summary.outstandingPayables));
        $('[data-kpi="inventoryValue"]').text(formatCurrency(summary.inventoryValue));
        $('[data-kpi="cashAndBankBalance"]').text(formatCurrency(summary.cashAndBankBalance));
    }

    function renderDailySalesChart(dailySales) {
        const ctx = document.getElementById('dailySalesChart');
        if (!ctx || typeof Chart === 'undefined') {
            return;
        }

        const labels = (dailySales || []).map(function (p) { return p.label; });
        const data = (dailySales || []).map(function (p) { return p.cartons; });

        if (dailySalesChart) {
            dailySalesChart.destroy();
        }

        dailySalesChart = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [{
                    label: 'Cartons Sold',
                    data: data,
                    backgroundColor: 'rgba(13, 110, 253, 0.75)',
                    borderRadius: 4
                }]
            },
            options: {
                responsive: true,
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: {
                            label: function (context) {
                                return 'Cartons: ' + Number(context.parsed.y).toLocaleString('en-PK', {
                                    minimumFractionDigits: 0,
                                    maximumFractionDigits: 2
                                });
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        ticks: {
                            maxRotation: 0,
                            autoSkip: true,
                            maxTicksLimit: 10
                        }
                    },
                    y: {
                        beginAtZero: true,
                        ticks: {
                            callback: function (value) {
                                return Number(value).toLocaleString('en-PK');
                            }
                        }
                    }
                }
            }
        });
    }

    function renderProfitLossChart(monthlyProfitLoss) {
        const ctx = document.getElementById('profitLossChart');
        if (!ctx || typeof Chart === 'undefined') {
            return;
        }

        const labels = (monthlyProfitLoss || []).map(function (p) { return p.label; });
        const data = (monthlyProfitLoss || []).map(function (p) { return p.netProfit; });
        const colors = data.map(function (value) {
            return value >= 0 ? 'rgba(25, 135, 84, 0.85)' : 'rgba(220, 53, 69, 0.85)';
        });

        if (profitLossChart) {
            profitLossChart.destroy();
        }

        profitLossChart = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [{
                    label: 'Net Profit / Loss (PKR)',
                    data: data,
                    backgroundColor: colors,
                    borderRadius: 4
                }]
            },
            options: {
                responsive: true,
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: {
                            label: function (context) {
                                var point = monthlyProfitLoss[context.dataIndex] || {};
                                var lines = [
                                    'Net: ' + formatCurrency(context.parsed.y),
                                    'Revenue: ' + formatCurrency(point.revenue || 0),
                                    'Expenses: ' + formatCurrency(point.expenses || 0)
                                ];
                                return lines;
                            }
                        }
                    }
                },
                scales: {
                    y: {
                        ticks: {
                            callback: function (value) {
                                return formatCompactCurrency(value);
                            }
                        }
                    }
                }
            }
        });
    }

    function renderChart(monthlySales) {
        const ctx = document.getElementById('salesChart');
        if (!ctx || typeof Chart === 'undefined') {
            return;
        }

        const labels = monthlySales.map(function (p) { return p.label; });
        const data = monthlySales.map(function (p) { return p.cartons; });

        if (salesChart) {
            salesChart.destroy();
        }

        salesChart = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [{
                    label: 'Cartons Sold',
                    data: data,
                    backgroundColor: 'rgba(13, 110, 253, 0.7)',
                    borderRadius: 4
                }]
            },
            options: {
                responsive: true,
                plugins: { legend: { display: false } },
                scales: {
                    y: {
                        beginAtZero: true,
                        ticks: {
                            callback: function (value) {
                                return Number(value).toLocaleString('en-PK');
                            }
                        }
                    }
                }
            }
        });
    }

    function renderTopCustomers(customers) {
        const $list = $('#top-customers-list');
        $list.empty();

        if (!customers || customers.length === 0) {
            $list.append('<li class="list-group-item text-muted small">No outstanding balances.</li>');
            return;
        }

        var debitRows = customers.filter(function (c) {
            return (c.balanceSide || 'Dr') === 'Dr';
        });
        var creditRows = customers.filter(function (c) {
            return (c.balanceSide || '') === 'Cr';
        });

        function appendCustomer(c) {
            var side = c.balanceSide || 'Dr';
            var amount = Math.abs(parseFloat(c.balance) || 0);
            var badgeClass = side === 'Dr' ? 'bg-primary' : 'bg-success';
            $list.append(
                '<li class="list-group-item d-flex justify-content-between align-items-center px-0">' +
                '<span><strong>' + $('<div>').text(c.buyerName).html() + '</strong>' +
                '<br><small class="text-muted">' + $('<div>').text(c.buyerId).html() + '</small></span>' +
                '<span class="text-end">' +
                '<span class="badge ' + badgeClass + ' me-1">' + side + '</span>' +
                '<span class="text-currency fw-semibold">' + formatCurrency(amount) + '</span>' +
                '</span></li>'
            );
        }

        if (debitRows.length) {
            $list.append('<li class="list-group-item px-0 py-1"><small class="text-muted fw-semibold">Debit (Receivable)</small></li>');
            debitRows.forEach(appendCustomer);
        }

        if (creditRows.length) {
            $list.append('<li class="list-group-item px-0 py-1' + (debitRows.length ? ' border-top mt-1' : '') + '"><small class="text-muted fw-semibold">Credit</small></li>');
            creditRows.forEach(appendCustomer);
        }
    }

    function renderLowStock(items) {
        const $list = $('#low-stock-list');
        $list.empty();

        if (!items || items.length === 0) {
            $list.append('<li class="list-group-item text-muted small">No low stock alerts.</li>');
            return;
        }

        items.forEach(function (item) {
            $list.append(
                '<li class="list-group-item px-0">' +
                '<div class="d-flex justify-content-between">' +
                '<span><strong class="text-danger">' + $('<div>').text(item.itemName).html() + '</strong>' +
                '<br><small class="text-muted">' + $('<div>').text(item.itemCode).html() + '</small></span>' +
                '<span class="text-end"><span class="badge bg-danger">' +
                item.currentStock + ' ' + $('<div>').text(item.unit).html() +
                '</span><br><small class="text-muted">Min: ' + item.minimumStock + '</small></span>' +
                '</div></li>'
            );
        });
    }

    function renderRecentInvoices(invoices) {
        const $tbody = $('#recent-invoices-table tbody');
        $tbody.empty();

        if (!invoices || invoices.length === 0) {
            $tbody.append('<tr><td colspan="5" class="text-muted text-center">No invoices yet.</td></tr>');
            return;
        }

        invoices.forEach(function (inv) {
            $tbody.append(
                '<tr>' +
                '<td>' + $('<div>').text(inv.invoiceNumber).html() + '</td>' +
                '<td>' + $('<div>').text(inv.customerName).html() + '</td>' +
                '<td>' + formatDate(inv.invoiceDate) + '</td>' +
                '<td class="text-end text-currency">' + formatCurrency(inv.netTotal) + '</td>' +
                '<td><span class="badge ' + inv.statusBadgeClass + '">' +
                $('<div>').text(inv.status).html() + '</span></td>' +
                '</tr>'
            );
        });
    }

    function renderBankClosingBalances(rows) {
        var $card = $('#bank-closing-balances-card');
        var $tbody = $('#bank-closing-balances-table tbody');
        $tbody.empty();

        if (!rows || rows.length === 0) {
            $card.addClass('d-none');
            return;
        }

        $card.removeClass('d-none');
        rows.forEach(function (row) {
            $tbody.append(
                '<tr>' +
                '<td><code>' + $('<div>').text(row.accountNumber).html() + '</code> ' +
                $('<span>').text(row.accountName).html() + '</td>' +
                '<td class="text-end text-currency fw-semibold">' + formatCurrency(row.closingBalance) + '</td>' +
                '</tr>'
            );
        });
    }

    function renderApClosingBalances(rows) {
        var $card = $('#ap-closing-balances-card');
        var $tbody = $('#ap-closing-balances-table tbody');
        $tbody.empty();

        if (!rows || rows.length === 0) {
            $card.addClass('d-none');
            return;
        }

        $card.removeClass('d-none');
        rows.forEach(function (row) {
            var rowClass = row.isCurrentCompany ? 'table-primary' : '';
            $tbody.append(
                '<tr class="' + rowClass + '">' +
                '<td>' + $('<div>').text(row.companyName).html() +
                (row.isCurrentCompany ? ' <span class="badge bg-primary">Current</span>' : '') +
                '</td>' +
                '<td class="text-end text-currency fw-semibold">' + formatCurrency(row.closingBalance) + '</td>' +
                '</tr>'
            );
        });
    }

    function showError() {
        $('[data-kpi]').text('—');
        $('#top-customers-list').html('<li class="list-group-item text-danger small">Failed to load dashboard.</li>');
        $('#low-stock-list').html('<li class="list-group-item text-danger small">Failed to load.</li>');
        $('#recent-invoices-table tbody').html(
            '<tr><td colspan="5" class="text-danger text-center">Failed to load invoices.</td></tr>'
        );
    }

    function loadDashboard() {
        $.ajax({ url: '/api/dashboard', dataType: 'json', cache: false })
            .done(function (data) {
                setKpis(data.summary);
                renderDailySalesChart(data.dailySales);
                renderProfitLossChart(data.monthlyProfitLoss);
                renderChart(data.monthlySales);
                renderTopCustomers(data.topCustomers);
                renderBankClosingBalances(data.bankClosingBalances);
                renderApClosingBalances(data.apClosingBalances);
                renderLowStock(data.lowStockItems);
                renderRecentInvoices(data.recentInvoices);
            })
            .fail(showError);
    }

    $(function () {
        loadDashboard();
    });
})();
