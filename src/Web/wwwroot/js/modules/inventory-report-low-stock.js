(function () {
    'use strict';

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
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
            'Generated: ' + formatDateTime(data.generatedAt) + ' — ' + data.itemCount + ' item(s) need attention'
        );

        var $tbody = $('#report-lines');
        $tbody.empty();

        if (!data.lines || data.lines.length === 0) {
            $tbody.append('<tr><td colspan="8" class="text-muted text-center">All items are above minimum stock levels.</td></tr>');
            $('#report-footer').addClass('d-none');
            return;
        }

        data.lines.forEach(function (line) {
            $tbody.append(
                '<tr class="table-warning">' +
                '<td><code>' + escapeHtml(line.itemCode) + '</code></td>' +
                '<td>' + escapeHtml(line.itemName) + '</td>' +
                '<td>' + (line.categoryName ? escapeHtml(line.categoryName) : '—') + '</td>' +
                '<td>' + escapeHtml(line.unitSymbol) + '</td>' +
                '<td class="text-end fw-semibold">' + formatQty(line.currentStock) + '</td>' +
                '<td class="text-end">' + formatQty(line.minimumStock) + '</td>' +
                '<td class="text-end">' + formatQty(line.reorderLevel) + '</td>' +
                '<td class="text-end text-danger">' + formatQty(line.shortfall) + '</td>' +
                '</tr>'
            );
        });

        $('#report-item-count').text(data.itemCount);
        $('#report-footer').removeClass('d-none');
    }

    function loadReport() {
        $.getJSON('/api/inventory-reports/low-stock')
            .done(renderReport)
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Failed to load report.'));
            });
    }

    $(function () {
        $.getJSON('/api/company/current')
            .done(loadReport)
            .fail(function () {
                $('#report-company-warning')
                    .removeClass('d-none')
                    .text('Select a company from the top navbar to run this report.');
                $('#report-lines').html('<tr><td colspan="8" class="text-muted text-center">Select a company first.</td></tr>');
                $('#report-generated').text('');
            });

        $('#btn-load-report').on('click', loadReport);
        $('#btn-print-report').on('click', function () {
            window.print();
        });
    });
})();
