(function () {
    'use strict';

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function formatAmount(value) {
        var num = parseFloat(value) || 0;
        return num.toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function formatQty(value) {
        var num = parseFloat(value) || 0;
        return num.toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function formatDateTime(value) {
        var d = new Date(value);
        if (Number.isNaN(d.getTime())) {
            return '';
        }
        return d.toLocaleString('en-GB');
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        if (!body) {
            return fallback;
        }
        return body.message || body.Message || fallback;
    }

    function renderReport(data) {
        $('#report-generated').text(
            'Generated: ' + formatDateTime(data.generatedAt) + ' — ' + data.itemCount + ' item(s)'
        );

        var $tbody = $('#report-lines');
        $tbody.empty();

        if (!data.lines || data.lines.length === 0) {
            $tbody.append('<tr><td colspan="9" class="text-muted text-center">No items found.</td></tr>');
            $('#report-footer').addClass('d-none');
            return;
        }

        data.lines.forEach(function (line) {
            $tbody.append(
                '<tr>' +
                '<td><code>' + escapeHtml(line.itemCode) + '</code></td>' +
                '<td>' + escapeHtml(line.itemName) + '</td>' +
                '<td>' + (line.categoryName ? escapeHtml(line.categoryName) : '—') + '</td>' +
                '<td>' + escapeHtml(line.unitSymbol) + '</td>' +
                '<td class="text-end">' + formatQty(line.currentStock) + '</td>' +
                '<td class="text-end">' + formatQty(line.minimumStock) + '</td>' +
                '<td class="text-end">' + formatQty(line.reorderLevel) + '</td>' +
                '<td class="text-end">' + formatAmount(line.purchaseRate) + '</td>' +
                '<td class="text-end fw-semibold">' + formatAmount(line.stockValue) + '</td>' +
                '</tr>'
            );
        });

        $('#report-total-value').text(formatAmount(data.totalStockValue));
        $('#report-footer').removeClass('d-none');
    }

    function loadCategories() {
        return $.getJSON('/api/inventory-reports/categories').done(function (categories) {
            var $select = $('#filter-category');
            (categories || []).forEach(function (c) {
                $select.append($('<option></option>').val(c.id).text(c.name));
            });
        });
    }

    function loadReport() {
        var categoryId = $('#filter-category').val();
        var activeOnly = $('#filter-active-only').is(':checked');
        var params = { activeOnly: activeOnly };

        if (categoryId) {
            params.categoryId = categoryId;
        }

        $.getJSON('/api/inventory-reports/stock-summary', params)
            .done(renderReport)
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Failed to load report.'));
            });
    }

    $(function () {
        var today = new Date();

        $.getJSON('/api/company/current')
            .done(function () {
                loadCategories().always(loadReport);
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
