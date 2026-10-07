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

    function formatRemaining(value, isCarton) {
        var num = parseFloat(value);
        if (!Number.isFinite(num)) {
            num = 0;
        }
        return num.toLocaleString('en-PK', {
            minimumFractionDigits: isCarton ? 0 : 2,
            maximumFractionDigits: isCarton ? 0 : 2
        });
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

    function dateKey(value) {
        if (!value) {
            return '';
        }
        if (typeof value === 'string' && value.length >= 10 && value.charAt(4) === '-') {
            return value.slice(0, 10);
        }
        var d = new Date(value);
        if (Number.isNaN(d.getTime())) {
            return '';
        }
        return d.getFullYear() + '-' +
            String(d.getMonth() + 1).padStart(2, '0') + '-' +
            String(d.getDate()).padStart(2, '0');
    }

    function balanceKey(line, includeLot) {
        var itemCode = readValue(line, 'itemCode', 'ItemCode') || '';
        var stackNo = readValue(line, 'stackNo', 'StackNo') || '';
        if (!includeLot) {
            return itemCode + '|' + stackNo;
        }
        return itemCode + '|' + stackNo + '|' + (readValue(line, 'lotNo', 'LotNo') || '');
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

        var lines = data.lines || data.Lines || [];
        if (lines.length === 0) {
            $tbody.append('<tr><td colspan="14" class="text-muted text-center">No transactions in this period.</td></tr>');
            $('#report-footer').addClass('d-none');
            return;
        }

        var periodFrom = dateKey(readValue(data, 'fromDate', 'FromDate'));
        var lastWeight = {};
        var lastCartons = {};

        lines.forEach(function (line) {
            var transactionDate = readValue(line, 'transactionDate', 'TransactionDate');
            var referenceNo = readValue(line, 'referenceNo', 'ReferenceNo');
            var vendorRefNo = readValue(line, 'vendorRefNo', 'VendorRefNo');
            var inPeriod = !periodFrom || dateKey(transactionDate) >= periodFrom;
            var remainingWeight = parseFloat(readValue(line, 'remainingWeight', 'RemainingWeight')) || 0;
            var remainingCartons = parseFloat(readValue(line, 'remainingCartons', 'RemainingCartons')) || 0;
            var weightClass = 'text-end fw-semibold' + (remainingWeight < 0 ? ' text-danger' : '');
            var cartonClass = 'text-end fw-semibold' + (remainingCartons < 0 ? ' text-danger' : '');
            if (inPeriod) {
                lastWeight[balanceKey(line, true)] = remainingWeight;
                lastCartons[balanceKey(line, false)] = remainingCartons;
            }

            $tbody.append(
                '<tr>' +
                '<td>' + formatDate(transactionDate) + '</td>' +
                '<td>' + (referenceNo ? '<code>' + escapeHtml(referenceNo) + '</code>' : '—') + '</td>' +
                '<td>' + (vendorRefNo ? escapeHtml(vendorRefNo) : '—') + '</td>' +
                '<td>' + escapeHtml(readValue(line, 'transactionType', 'TransactionType') || '') + '</td>' +
                '<td><code>' + escapeHtml(readValue(line, 'itemCode', 'ItemCode') || '') + '</code> ' +
                    escapeHtml(readValue(line, 'itemName', 'ItemName') || '') + '</td>' +
                '<td>' + escapeHtml(readValue(line, 'stackNo', 'StackNo') || '—') + '</td>' +
                '<td>' + escapeHtml(readValue(line, 'lotNo', 'LotNo') || '—') + '</td>' +
                '<td>' + escapeHtml(readValue(line, 'warehouseName', 'WarehouseName') || '') + '</td>' +
                '<td class="text-end text-success">' + formatQty(readValue(line, 'qtyIn', 'QtyIn'), false) + '</td>' +
                '<td class="text-end text-danger">' + formatQty(readValue(line, 'qtyOut', 'QtyOut'), false) + '</td>' +
                '<td class="text-end text-success">' + formatQty(readValue(line, 'cartonsIn', 'CartonsIn'), true) + '</td>' +
                '<td class="text-end text-danger">' + formatQty(readValue(line, 'cartonsOut', 'CartonsOut'), true) + '</td>' +
                '<td class="' + weightClass + '">' + (inPeriod ? formatRemaining(remainingWeight, false) : '—') + '</td>' +
                '<td class="' + cartonClass + '">' + (inPeriod ? formatRemaining(remainingCartons, true) : '—') + '</td>' +
                '</tr>'
            );
        });

        var totalRemainingWeight = Object.keys(lastWeight).reduce(function (sum, key) {
            return sum + lastWeight[key];
        }, 0);
        var totalRemainingCartons = Object.keys(lastCartons).reduce(function (sum, key) {
            return sum + lastCartons[key];
        }, 0);

        $('#report-total-in').text(formatAmount(readValue(data, 'totalQtyIn', 'TotalQtyIn')));
        $('#report-total-out').text(formatAmount(readValue(data, 'totalQtyOut', 'TotalQtyOut')));
        $('#report-total-ctn-in').text(formatQty(readValue(data, 'totalCartonsIn', 'TotalCartonsIn'), true));
        $('#report-total-ctn-out').text(formatQty(readValue(data, 'totalCartonsOut', 'TotalCartonsOut'), true));
        $('#report-total-remaining-weight').text(formatRemaining(totalRemainingWeight, false));
        $('#report-total-remaining-ctn').text(formatRemaining(totalRemainingCartons, true));
        $('#report-footer').removeClass('d-none');
    }

    function loadLookups() {
        if (window.initPaAjaxSelect2) {
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
