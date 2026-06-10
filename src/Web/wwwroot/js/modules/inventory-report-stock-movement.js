(function () {
    'use strict';

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function formatAmount(value) {
        var num = parseFloat(value) || 0;
        return num.toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function formatQty(value, isCarton) {
        var num = parseFloat(value) || 0;
        if (num === 0) {
            return '—';
        }
        return num.toLocaleString('en-PK', {
            minimumFractionDigits: isCarton ? 0 : 2,
            maximumFractionDigits: isCarton ? 0 : 2
        });
    }

    function formatDate(value) {
        var d = new Date(value);
        if (Number.isNaN(d.getTime())) {
            return value;
        }
        return d.toLocaleDateString('en-GB');
    }

    function toInputDate(date) {
        return date.getFullYear() + '-' +
            String(date.getMonth() + 1).padStart(2, '0') + '-' +
            String(date.getDate()).padStart(2, '0');
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        if (!body) {
            return fallback;
        }
        return body.message || body.Message || fallback;
    }

    function renderReport(data) {
        $('#report-period').text(
            'Period: ' + formatDate(data.fromDate) + ' to ' + formatDate(data.toDate) +
            ' — ' + data.transactionCount + ' transaction(s)'
        );

        var filters = [];
        if (data.itemLabel) {
            filters.push('Item: ' + data.itemLabel);
        }
        if (data.warehouseLabel) {
            filters.push('Warehouse: ' + data.warehouseLabel);
        }
        $('#report-filters').text(filters.length ? filters.join(' | ') : 'All items and warehouses');

        var $tbody = $('#report-lines');
        $tbody.empty();

        if (!data.lines || data.lines.length === 0) {
            $tbody.append('<tr><td colspan="13" class="text-muted text-center">No transactions in this period.</td></tr>');
            $('#report-footer').addClass('d-none');
            return;
        }

        data.lines.forEach(function (line) {
            $tbody.append(
                '<tr>' +
                '<td>' + formatDate(line.transactionDate) + '</td>' +
                '<td>' + (line.referenceNo ? '<code>' + escapeHtml(line.referenceNo) + '</code>' : '—') + '</td>' +
                '<td>' + escapeHtml(line.transactionType) + '</td>' +
                '<td><code>' + escapeHtml(line.itemCode) + '</code> ' + escapeHtml(line.itemName) + '</td>' +
                '<td>' + escapeHtml(line.stackNo || '—') + '</td>' +
                '<td>' + escapeHtml(line.lotNo || '—') + '</td>' +
                '<td>' + escapeHtml(line.warehouseName) + '</td>' +
                '<td class="text-end text-success">' + formatQty(line.qtyIn, false) + '</td>' +
                '<td class="text-end text-danger">' + formatQty(line.qtyOut, false) + '</td>' +
                '<td class="text-end text-success">' + formatQty(line.cartonsIn, true) + '</td>' +
                '<td class="text-end text-danger">' + formatQty(line.cartonsOut, true) + '</td>' +
                '<td class="text-end">' + formatQty(line.adjustmentQty, false) + '</td>' +
                '<td class="text-end">' + formatAmount(line.totalCost) + '</td>' +
                '</tr>'
            );
        });

        $('#report-total-in').text(formatAmount(data.totalQtyIn));
        $('#report-total-out').text(formatAmount(data.totalQtyOut));
        $('#report-total-ctn-in').text(formatQty(data.totalCartonsIn, true));
        $('#report-total-ctn-out').text(formatQty(data.totalCartonsOut, true));
        $('#report-footer').removeClass('d-none');
    }

    function loadLookups() {
        return $.when(
            $.getJSON('/api/inventory-reports/items'),
            $.getJSON('/api/inventory-reports/warehouses')
        ).then(function (itemsRes, warehousesRes) {
            var $item = $('#filter-item');
            (itemsRes[0] || []).forEach(function (i) {
                $item.append($('<option></option>').val(i.id).text(i.itemCode + ' — ' + i.itemName));
            });

            var $warehouse = $('#filter-warehouse');
            (warehousesRes[0] || []).forEach(function (w) {
                $warehouse.append($('<option></option>').val(w.id).text(w.code + ' — ' + w.name));
            });

            if ($.fn.select2) {
                $('#filter-item, #filter-warehouse').select2({ theme: 'bootstrap-5', width: '100%' });
            }
        });
    }

    function loadReport() {
        var from = $('#filter-from').val();
        var to = $('#filter-to').val();

        if (!from || !to) {
            alert('Please select from and to dates.');
            return;
        }

        var params = { fromDate: from, toDate: to };
        var itemId = parseInt($('#filter-item').val(), 10);
        var warehouseId = parseInt($('#filter-warehouse').val(), 10);

        if (itemId > 0) {
            params.itemId = itemId;
        }
        if (warehouseId > 0) {
            params.warehouseId = warehouseId;
        }

        $.getJSON('/api/inventory-reports/stock-movement', params)
            .done(renderReport)
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Failed to load report.'));
            });
    }

    $(function () {
        var today = new Date();
        var monthStart = new Date(today.getFullYear(), today.getMonth(), 1);
        var openingStockStart = new Date(2026, 4, 31);
        var defaultFrom = monthStart < openingStockStart ? monthStart : openingStockStart;

        $('#filter-from').val(toInputDate(defaultFrom));
        $('#filter-to').val(toInputDate(today));

        $.getJSON('/api/company/current')
            .done(function () {
                loadLookups();
            })
            .fail(function () {
                $('#report-company-warning')
                    .removeClass('d-none')
                    .text('Select a company from the top navbar to run this report.');
            });

        $('#btn-load-report').on('click', loadReport);
        $('#btn-print-report').on('click', function () {
            window.print();
        });
    });
})();
