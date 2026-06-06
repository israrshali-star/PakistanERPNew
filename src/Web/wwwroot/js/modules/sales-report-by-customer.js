(function () {
    'use strict';

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function formatAmount(value) {
        var num = parseFloat(value) || 0;
        return num.toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
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
            ' — ' + data.customerCount + ' customer(s)'
        );

        var $tbody = $('#report-lines');
        $tbody.empty();

        if (!data.lines || data.lines.length === 0) {
            $tbody.append('<tr><td colspan="7" class="text-muted text-center">No sales found.</td></tr>');
            $('#report-footer').addClass('d-none');
            return;
        }

        data.lines.forEach(function (line) {
            $tbody.append(
                '<tr>' +
                '<td><code>' + escapeHtml(line.customerCode) + '</code></td>' +
                '<td>' + escapeHtml(line.customerName) + '</td>' +
                '<td class="text-end">' + line.invoiceCount + '</td>' +
                '<td class="text-end">' + formatAmount(line.subTotal) + '</td>' +
                '<td class="text-end">' + formatAmount(line.discountAmount) + '</td>' +
                '<td class="text-end">' + formatAmount(line.taxAmount) + '</td>' +
                '<td class="text-end fw-semibold">' + formatAmount(line.netTotal) + '</td>' +
                '</tr>'
            );
        });

        $('#report-total-subtotal').text(formatAmount(data.totalSubTotal));
        $('#report-total-discount').text(formatAmount(data.totalDiscount));
        $('#report-total-tax').text(formatAmount(data.totalTax));
        $('#report-total-net').text(formatAmount(data.totalNet));
        $('#report-footer').removeClass('d-none');
    }

    function loadCustomers() {
        return $.getJSON('/api/sales-reports/customers').done(function (customers) {
            var $select = $('#filter-customer');
            (customers || []).forEach(function (c) {
                $select.append($('<option></option>').val(c.id).text(c.buyerId + ' — ' + c.name));
            });

            if ($.fn.select2) {
                $('#filter-customer').select2({ theme: 'bootstrap-5', width: '100%' });
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

        var params = {
            fromDate: from,
            toDate: to,
            postedOnly: $('#filter-posted-only').is(':checked')
        };

        var customerId = parseInt($('#filter-customer').val(), 10);
        if (customerId > 0) {
            params.customerId = customerId;
        }

        $.getJSON('/api/sales-reports/by-customer', params)
            .done(renderReport)
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Failed to load report.'));
            });
    }

    $(function () {
        var today = new Date();
        var monthStart = new Date(today.getFullYear(), today.getMonth(), 1);

        $('#filter-from').val(toInputDate(monthStart));
        $('#filter-to').val(toInputDate(today));

        $.getJSON('/api/company/current')
            .done(function () {
                loadCustomers().always(loadReport);
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
