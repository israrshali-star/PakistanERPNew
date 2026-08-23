(function () {
    'use strict';

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function toNumber(value) {
        var num = parseFloat(value);
        return Number.isFinite(num) ? num : 0;
    }

    function formatAmount(value) {
        return toNumber(value).toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function formatQty(value, isCarton) {
        var num = toNumber(value);
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

    function readValue(obj, camelName, pascalName) {
        if (!obj) {
            return undefined;
        }
        if (obj[camelName] !== undefined && obj[camelName] !== null) {
            return obj[camelName];
        }
        if (obj[pascalName] !== undefined && obj[pascalName] !== null) {
            return obj[pascalName];
        }
        return undefined;
    }

    function renderMovements(lines) {
        var $section = $('#report-movements-section');
        var $container = $('#report-movements');
        $container.empty();

        var detailLines = (lines || []).filter(function (line) {
            return (line.movements || line.Movements || []).length > 0
                || toNumber(readValue(line, 'openingQty', 'OpeningQty')) !== 0
                || toNumber(readValue(line, 'openingCartons', 'OpeningCartons')) !== 0;
        });

        if (detailLines.length === 0) {
            $section.addClass('d-none');
            return;
        }

        detailLines.forEach(function (line) {
            var stackNo = readValue(line, 'stackNo', 'StackNo') || '—';
            var itemCode = readValue(line, 'itemCode', 'ItemCode') || '';
            var itemName = readValue(line, 'itemName', 'ItemName') || '';
            var lotNo = readValue(line, 'lotNo', 'LotNo') || '—';
            var title = 'Stack # ' + escapeHtml(stackNo)
                + ' — <code>' + escapeHtml(itemCode) + '</code> ' + escapeHtml(itemName)
                + ' | Lot: ' + escapeHtml(lotNo);

            var runningQty = toNumber(readValue(line, 'openingQty', 'OpeningQty'));
            var runningCtn = toNumber(readValue(line, 'openingCartons', 'OpeningCartons'));
            var rows = '<tr class="table-light">' +
                '<td>—</td>' +
                '<td>Opening</td>' +
                '<td>—</td>' +
                '<td>—</td>' +
                '<td class="text-end">' + formatQty(runningQty, false) + '</td>' +
                '<td class="text-end">—</td>' +
                '<td class="text-end">' + formatQty(runningCtn, true) + '</td>' +
                '<td class="text-end">—</td>' +
                '<td class="text-end fw-semibold">' + formatQty(runningQty, false) + '</td>' +
                '<td class="text-end fw-semibold">' + formatQty(runningCtn, true) + '</td>' +
                '</tr>';

            (line.movements || line.Movements || []).forEach(function (m) {
                var qtyIn = toNumber(readValue(m, 'qtyIn', 'QtyIn'));
                var qtyOut = toNumber(readValue(m, 'qtyOut', 'QtyOut'));
                var ctnIn = toNumber(readValue(m, 'cartonsIn', 'CartonsIn'));
                var ctnOut = toNumber(readValue(m, 'cartonsOut', 'CartonsOut'));
                var adj = toNumber(readValue(m, 'adjustmentQty', 'AdjustmentQty'));
                runningQty = runningQty + qtyIn - qtyOut + adj;
                runningCtn = runningCtn + ctnIn - ctnOut;
                rows += '<tr>' +
                    '<td>' + formatDate(readValue(m, 'transactionDate', 'TransactionDate')) + '</td>' +
                    '<td>' + escapeHtml(readValue(m, 'transactionType', 'TransactionType') || '') + '</td>' +
                    '<td>' + (readValue(m, 'referenceNo', 'ReferenceNo')
                        ? '<code>' + escapeHtml(readValue(m, 'referenceNo', 'ReferenceNo')) + '</code>'
                        : '—') + '</td>' +
                    '<td>' + escapeHtml(readValue(m, 'vendorRefNo', 'VendorRefNo') || '—') + '</td>' +
                    '<td class="text-end text-success">' + (qtyIn ? formatQty(qtyIn, false) : '—') + '</td>' +
                    '<td class="text-end text-danger">' + (qtyOut ? formatQty(qtyOut, false) : '—') + '</td>' +
                    '<td class="text-end text-success">' + (ctnIn ? formatQty(ctnIn, true) : '—') + '</td>' +
                    '<td class="text-end text-danger">' + (ctnOut ? formatQty(ctnOut, true) : '—') + '</td>' +
                    '<td class="text-end">' + formatQty(runningQty, false) + '</td>' +
                    '<td class="text-end">' + formatQty(runningCtn, true) + '</td>' +
                    '</tr>';
            });

            $container.append(
                '<div class="mb-4">' +
                '<div class="small fw-semibold text-primary mb-2">' + title + '</div>' +
                '<div class="table-responsive">' +
                '<table class="table table-sm table-bordered mb-0">' +
                '<thead class="table-light">' +
                '<tr>' +
                '<th>Date</th><th>Type</th><th>Ref #</th><th>Vendor Ref #</th>' +
                '<th class="text-end">Qty In</th><th class="text-end">Qty Out</th>' +
                '<th class="text-end">Ctn In</th><th class="text-end">Ctn Out</th>' +
                '<th class="text-end">Balance Qty</th><th class="text-end">Balance Ctn</th>' +
                '</tr></thead><tbody>' + rows + '</tbody></table></div></div>'
            );
        });

        $section.removeClass('d-none');
    }

    function renderReport(data) {
        $('#report-period').text(
            'Period: ' + formatDate(data.fromDate || data.FromDate) + ' to ' +
            formatDate(data.toDate || data.ToDate) +
            ' — ' + (data.stackCount || data.StackCount || 0) + ' stack(s)'
        );

        var filters = [];
        var stackNo = data.stackNo || data.StackNo;
        if (stackNo) {
            filters.push('Stack #: ' + stackNo);
        }
        if (data.itemLabel || data.ItemLabel) {
            filters.push('Item: ' + (data.itemLabel || data.ItemLabel));
        }
        if (data.warehouseLabel || data.WarehouseLabel) {
            filters.push('Warehouse: ' + (data.warehouseLabel || data.WarehouseLabel));
        }
        $('#report-filters').text(filters.length ? filters.join(' | ') : 'All stacks, items, and warehouses');

        var $tbody = $('#report-lines');
        $tbody.empty();

        var lines = data.lines || data.Lines || [];
        if (lines.length === 0) {
            $tbody.append('<tr><td colspan="12" class="text-muted text-center">No stack movement in this period.</td></tr>');
            $('#report-footer').addClass('d-none');
            $('#report-movements-section').addClass('d-none');
            return;
        }

        lines.forEach(function (line) {
            var closingQty = toNumber(readValue(line, 'closingQty', 'ClosingQty'));
            var closingCtn = toNumber(readValue(line, 'closingCartons', 'ClosingCartons'));
            $tbody.append(
                '<tr>' +
                '<td>' + escapeHtml(readValue(line, 'stackNo', 'StackNo') || '—') + '</td>' +
                '<td>' + escapeHtml(readValue(line, 'vendorRefNo', 'VendorRefNo') || '—') + '</td>' +
                '<td><code>' + escapeHtml(readValue(line, 'itemCode', 'ItemCode') || '') + '</code> ' +
                    escapeHtml(readValue(line, 'itemName', 'ItemName') || '') + '</td>' +
                '<td>' + escapeHtml(readValue(line, 'lotNo', 'LotNo') || '—') + '</td>' +
                '<td class="text-end">' + formatQty(readValue(line, 'openingQty', 'OpeningQty'), false) + '</td>' +
                '<td class="text-end">' + formatQty(readValue(line, 'openingCartons', 'OpeningCartons'), true) + '</td>' +
                '<td class="text-end text-success">' + formatQty(readValue(line, 'qtyIn', 'QtyIn'), false) + '</td>' +
                '<td class="text-end text-danger">' + formatQty(readValue(line, 'qtyOut', 'QtyOut'), false) + '</td>' +
                '<td class="text-end text-success">' + formatQty(readValue(line, 'cartonsIn', 'CartonsIn'), true) + '</td>' +
                '<td class="text-end text-danger">' + formatQty(readValue(line, 'cartonsOut', 'CartonsOut'), true) + '</td>' +
                '<td class="text-end fw-semibold' + (closingQty < 0 ? ' text-danger' : '') + '">' +
                    formatQty(closingQty, false) + '</td>' +
                '<td class="text-end fw-semibold' + (closingCtn < 0 ? ' text-danger' : '') + '">' +
                    formatQty(closingCtn, true) + '</td>' +
                '</tr>'
            );
        });

        $('#report-total-opening-qty').text(formatQty(readValue(data, 'totalOpeningQty', 'TotalOpeningQty'), false));
        $('#report-total-opening-ctn').text(formatQty(readValue(data, 'totalOpeningCartons', 'TotalOpeningCartons'), true));
        $('#report-total-qty-in').text(formatQty(readValue(data, 'totalQtyIn', 'TotalQtyIn'), false));
        $('#report-total-qty-out').text(formatQty(readValue(data, 'totalQtyOut', 'TotalQtyOut'), false));
        $('#report-total-ctn-in').text(formatQty(readValue(data, 'totalCartonsIn', 'TotalCartonsIn'), true));
        $('#report-total-ctn-out').text(formatQty(readValue(data, 'totalCartonsOut', 'TotalCartonsOut'), true));
        $('#report-total-closing-qty').text(formatQty(readValue(data, 'totalClosingQty', 'TotalClosingQty'), false));
        $('#report-total-closing-ctn').text(formatQty(readValue(data, 'totalClosingCartons', 'TotalClosingCartons'), true));
        $('#report-footer').removeClass('d-none');

        var selectedStack = data.stackNo || data.StackNo;
        if (selectedStack || lines.length <= 15) {
            renderMovements(lines);
        } else {
            $('#report-movements-section').addClass('d-none');
            $('#report-movements').empty();
        }
    }

    function loadLookups() {
        if (window.initPaAjaxSelect2) {
            window.initPaAjaxSelect2($('#filter-stack'), {
                entity: 'stack',
                placeholder: 'Type to search stack #',
                allowClear: true
            });
            window.initPaAjaxSelect2($('#filter-item'), {
                entity: 'item',
                placeholder: 'Type to search item',
                allowClear: true
            });
            window.initPaAjaxSelect2($('#filter-warehouse'), {
                entity: 'warehouse',
                placeholder: 'Type to search warehouse',
                allowClear: true
            });
            return $.Deferred().resolve().promise();
        }

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
                $('#filter-item, #filter-warehouse, #filter-stack').select2({ theme: 'bootstrap-5', width: '100%' });
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
        var stackNo = $('#filter-stack').val();

        if (itemId > 0) {
            params.itemId = itemId;
        }
        if (warehouseId > 0) {
            params.warehouseId = warehouseId;
        }
        if (stackNo) {
            params.stackNo = stackNo;
        }

        $.getJSON('/api/inventory-reports/stack-movement', params)
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
