(function () {
    'use strict';

    let salesChart = null;

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

    function setKpis(summary) {
        $('[data-kpi="todaySales"]').text(formatCurrency(summary.todaySales));
        $('[data-kpi="monthSales"]').text(formatCurrency(summary.monthSales));
        $('[data-kpi="outstandingReceivables"]').text(formatCurrency(summary.outstandingReceivables));
        $('[data-kpi="outstandingPayables"]').text(formatCurrency(summary.outstandingPayables));
        $('[data-kpi="inventoryValue"]').text(formatCurrency(summary.inventoryValue));
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

        customers.forEach(function (c) {
            $list.append(
                '<li class="list-group-item d-flex justify-content-between align-items-center px-0">' +
                '<span><strong>' + $('<div>').text(c.buyerName).html() + '</strong>' +
                '<br><small class="text-muted">' + $('<div>').text(c.buyerId).html() + '</small></span>' +
                '<span class="text-currency fw-semibold">' + formatCurrency(c.balance) + '</span>' +
                '</li>'
            );
        });
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

    function showError() {
        $('[data-kpi]').text('—');
        $('#top-customers-list').html('<li class="list-group-item text-danger small">Failed to load dashboard.</li>');
        $('#low-stock-list').html('<li class="list-group-item text-danger small">Failed to load.</li>');
        $('#recent-invoices-table tbody').html(
            '<tr><td colspan="5" class="text-danger text-center">Failed to load invoices.</td></tr>'
        );
    }

    function loadDashboard() {
        $.getJSON('/api/dashboard')
            .done(function (data) {
                setKpis(data.summary);
                renderChart(data.monthlySales);
                renderTopCustomers(data.topCustomers);
                renderLowStock(data.lowStockItems);
                renderRecentInvoices(data.recentInvoices);
            })
            .fail(showError);
    }

    $(function () {
        loadDashboard();
    });
})();
