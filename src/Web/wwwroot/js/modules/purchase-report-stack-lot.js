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

    function readLineMetric(line, camelName, pascalName) {
        if (line[camelName] !== undefined && line[camelName] !== null) {
            return line[camelName];
        }
        if (line[pascalName] !== undefined && line[pascalName] !== null) {
            return line[pascalName];
        }
        return 0;
    }

    function formatDate(value) {
        var d = new Date(value);
        if (Number.isNaN(d.getTime())) {
            return value;
        }
        return d.toLocaleDateString('en-GB');
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        if (!body) {
            return fallback;
        }
        return body.message || body.Message || fallback;
    }

    function renderMovements(lines) {
        var $section = $('#report-movements-section');
        var $container = $('#report-movements');
        $container.empty();

        var hasMovements = (lines || []).some(function (line) {
            return line.movements && line.movements.length > 0;
        });

        if (!hasMovements) {
            $section.addClass('d-none');
            return;
        }

        lines.forEach(function (line) {
            if (!line.movements || line.movements.length === 0) {
                return;
            }

            var title = escapeHtml(line.itemCode) + ' — ' + escapeHtml(line.itemName)
                + ' | Stack: ' + (line.stackNo ? escapeHtml(line.stackNo) : '—')
                + ' | Lot: ' + (line.lotNo ? escapeHtml(line.lotNo) : '—');

            var rows = line.movements.map(function (m) {
                return '<tr>' +
                    '<td>' + escapeHtml(m.movementType) + '</td>' +
                    '<td><code>' + escapeHtml(m.referenceNumber) + '</code></td>' +
                    '<td>' + formatDate(m.date) + '</td>' +
                    '<td class="text-end">' + formatAmount(m.cartons) + '</td>' +
                    '<td class="text-end">' + formatAmount(m.weight) + '</td>' +
                    '<td class="text-end">' + formatAmount(m.amount) + '</td>' +
                    '</tr>';
            }).join('');

            $container.append(
                '<div class="mb-3">' +
                '<div class="small fw-semibold text-primary mb-2">' + title + '</div>' +
                '<div class="table-responsive">' +
                '<table class="table table-sm table-bordered mb-0">' +
                '<thead class="table-light">' +
                '<tr><th>Type</th><th>Reference</th><th>Date</th>' +
                '<th class="text-end">Cartons</th><th class="text-end">Weight</th><th class="text-end">Amount</th></tr>' +
                '</thead><tbody>' + rows + '</tbody></table></div></div>'
            );
        });

        $section.removeClass('d-none');
    }

    function renderReport(data) {
        var parts = [];
        if (data.itemLabel) {
            parts.push('Item: ' + data.itemLabel);
        }
        if (data.stackNo) {
            parts.push('Stack: ' + data.stackNo);
        }
        if (data.lotNo) {
            parts.push('Lot: ' + data.lotNo);
        }
        $('#report-filter-label').text(parts.length ? parts.join(' — ') : 'All items, stacks, and lots');

        var $tbody = $('#report-lines');
        $tbody.empty();

        if (!data.lines || data.lines.length === 0) {
            $tbody.append('<tr><td colspan="11" class="text-muted text-center">No stack/lot records found.</td></tr>');
            $('#report-footer').addClass('d-none');
            $('#report-movements-section').addClass('d-none');
            return;
        }

        var totalPurchasedCartons = 0;
        var totalPurchasedWeight = 0;
        var totalSoldCartons = 0;
        var totalSoldWeight = 0;
        var totalRemainingCartons = 0;
        var totalRemainingWeight = 0;

        data.lines.forEach(function (line) {
            var purchasedCartons = toNumber(readLineMetric(line, 'purchasedCartons', 'PurchasedCartons'));
            var purchasedWeight = toNumber(readLineMetric(line, 'purchasedWeight', 'PurchasedWeight'));
            var purchasedAmount = toNumber(readLineMetric(line, 'purchasedAmount', 'PurchasedAmount'));
            var soldCartons = toNumber(readLineMetric(line, 'soldCartons', 'SoldCartons'));
            var soldWeight = toNumber(readLineMetric(line, 'soldWeight', 'SoldWeight'));
            var soldAmount = toNumber(readLineMetric(line, 'soldAmount', 'SoldAmount'));
            var hasRemainingCartons = line.remainingCartons !== undefined && line.remainingCartons !== null
                || line.RemainingCartons !== undefined && line.RemainingCartons !== null;
            var hasRemainingWeight = line.remainingWeight !== undefined && line.remainingWeight !== null
                || line.RemainingWeight !== undefined && line.RemainingWeight !== null;
            var remainingCartons = hasRemainingCartons
                ? toNumber(readLineMetric(line, 'remainingCartons', 'RemainingCartons'))
                : purchasedCartons - soldCartons;
            var remainingWeight = hasRemainingWeight
                ? toNumber(readLineMetric(line, 'remainingWeight', 'RemainingWeight'))
                : purchasedWeight - soldWeight;

            totalPurchasedCartons += purchasedCartons;
            totalPurchasedWeight += purchasedWeight;
            totalSoldCartons += soldCartons;
            totalSoldWeight += soldWeight;
            totalRemainingCartons += remainingCartons;
            totalRemainingWeight += remainingWeight;

            var remainingCartonsClass = remainingCartons < 0 ? 'text-danger fw-semibold' : 'fw-semibold';
            var remainingWeightClass = remainingWeight < 0 ? 'text-danger fw-semibold' : 'fw-semibold';

            $tbody.append(
                '<tr>' +
                '<td><code>' + escapeHtml(line.itemCode || line.ItemCode) + '</code> ' + escapeHtml(line.itemName || line.ItemName) + '</td>' +
                '<td>' + ((line.stackNo || line.StackNo) ? escapeHtml(line.stackNo || line.StackNo) : '—') + '</td>' +
                '<td>' + ((line.lotNo || line.LotNo) ? escapeHtml(line.lotNo || line.LotNo) : '—') + '</td>' +
                '<td class="text-end">' + formatAmount(purchasedCartons) + '</td>' +
                '<td class="text-end">' + formatAmount(purchasedWeight) + '</td>' +
                '<td class="text-end">' + formatAmount(purchasedAmount) + '</td>' +
                '<td class="text-end">' + formatAmount(soldCartons) + '</td>' +
                '<td class="text-end">' + formatAmount(soldWeight) + '</td>' +
                '<td class="text-end">' + formatAmount(soldAmount) + '</td>' +
                '<td class="text-end ' + remainingCartonsClass + '">' + formatAmount(remainingCartons) + '</td>' +
                '<td class="text-end ' + remainingWeightClass + '">' + formatAmount(remainingWeight) + '</td>' +
                '</tr>'
            );
        });

        $('#report-total-purchased-cartons').text(formatAmount(readLineMetric(data, 'totalPurchasedCartons', 'TotalPurchasedCartons') || totalPurchasedCartons));
        $('#report-total-purchased-weight').text(formatAmount(readLineMetric(data, 'totalPurchasedWeight', 'TotalPurchasedWeight') || totalPurchasedWeight));
        $('#report-total-purchased-amount').text(formatAmount(readLineMetric(data, 'totalPurchasedAmount', 'TotalPurchasedAmount')));
        $('#report-total-sold-cartons').text(formatAmount(readLineMetric(data, 'totalSoldCartons', 'TotalSoldCartons') || totalSoldCartons));
        $('#report-total-sold-weight').text(formatAmount(readLineMetric(data, 'totalSoldWeight', 'TotalSoldWeight') || totalSoldWeight));
        $('#report-total-sold-amount').text(formatAmount(readLineMetric(data, 'totalSoldAmount', 'TotalSoldAmount')));
        $('#report-total-remaining-cartons').text(formatAmount(readLineMetric(data, 'totalRemainingCartons', 'TotalRemainingCartons') || totalRemainingCartons));
        $('#report-total-remaining-weight').text(formatAmount(readLineMetric(data, 'totalRemainingWeight', 'TotalRemainingWeight') || totalRemainingWeight));
        $('#report-footer').removeClass('d-none');

        renderMovements(data.lines);
    }

    function populateSelect($select, values, placeholder) {
        var current = $select.val();
        $select.find('option:not(:first)').remove();
        (values || []).forEach(function (value) {
            $select.append($('<option></option>').val(value).text(value));
        });
        if (current && (values || []).indexOf(current) >= 0) {
            $select.val(current);
        } else {
            $select.val('');
        }
    }

    function loadFilterLookups() {
        var itemId = parseInt($('#filter-item').val(), 10) || 0;
        var params = {};
        if (itemId > 0) {
            params.itemId = itemId;
        }

        return $.getJSON('/api/purchase-reports/stack-lot-filters', params)
            .done(function (data) {
                populateSelect($('#filter-stack'), data.stackNos, 'All stacks');
                populateSelect($('#filter-lot'), data.lotNos, 'All lots');
            });
    }

    function loadItems() {
        return $.getJSON('/api/purchase-reports/stack-lot-items').done(function (items) {
            var $select = $('#filter-item');
            (items || []).forEach(function (item) {
                $select.append(
                    $('<option></option>')
                        .val(item.id)
                        .text(item.itemCode + ' — ' + item.name)
                );
            });

            if ($.fn.select2) {
                $select.select2({ theme: 'bootstrap-5', width: '100%' });
            }
        });
    }

    function loadReport() {
        var params = {};
        var itemId = parseInt($('#filter-item').val(), 10);
        var lotNo = $('#filter-lot').val();
        var stackNo = $('#filter-stack').val();

        if (itemId > 0) {
            params.itemId = itemId;
        }
        if (lotNo) {
            params.lotNo = lotNo;
        }
        if (stackNo) {
            params.stackNo = stackNo;
        }

        $.getJSON('/api/purchase-reports/stack-lot-tracking', params)
            .done(renderReport)
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Failed to load report.'));
            });
    }

    $(function () {
        $.getJSON('/api/company/current')
            .done(function () {
                loadItems().always(function () {
                    loadFilterLookups().always(loadReport);
                });
            })
            .fail(function () {
                $('#report-company-warning')
                    .removeClass('d-none')
                    .text('Select a company from the top navbar to run this report.');
            });

        $('#filter-item').on('change', function () {
            loadFilterLookups();
        });

        $('#btn-load-report').on('click', loadReport);
        $('#btn-print-report').on('click', function () {
            window.print();
        });
    });
})();
