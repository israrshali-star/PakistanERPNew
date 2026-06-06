(function () {
    'use strict';

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

    function addRow(label, value, isTotal) {
        var cls = isTotal ? ' fw-bold table-light' : '';
        return '<tr class="' + cls + '"><td class="w-50">' + label + '</td><td class="text-end">' + formatAmount(value) + '</td></tr>';
    }

    function renderReport(data) {
        $('#report-period').text(
            'Period: ' + formatDate(data.fromDate) + ' to ' + formatDate(data.toDate) +
            ' — ' + data.billCount + ' bill(s)'
        );

        var $tbody = $('#report-summary');
        $tbody.empty();

        if (!data.billCount) {
            $tbody.append('<tr><td colspan="2" class="text-muted text-center">No bills found.</td></tr>');
            return;
        }

        $tbody.append(addRow('Total Quantity', data.totalQuantity));
        $tbody.append(addRow('Input Tax (Recoverable)', data.inputTaxAmount));
        $tbody.append(addRow('Net Purchase Amount', data.netAmount, true));
    }

    function loadVendors() {
        return $.getJSON('/api/purchase-reports/vendors').done(function (vendors) {
            var $select = $('#filter-vendor');
            (vendors || []).forEach(function (v) {
                $select.append($('<option></option>').val(v.id).text(v.vendorCode + ' — ' + v.name));
            });

            if ($.fn.select2) {
                $('#filter-vendor').select2({ theme: 'bootstrap-5', width: '100%' });
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
            approvedOnly: $('#filter-approved-only').is(':checked')
        };

        var vendorId = parseInt($('#filter-vendor').val(), 10);
        if (vendorId > 0) {
            params.vendorId = vendorId;
        }

        $.getJSON('/api/purchase-reports/input-tax-summary', params)
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
                loadVendors().always(loadReport);
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
