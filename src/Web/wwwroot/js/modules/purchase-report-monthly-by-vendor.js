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

    function renderRefList(values) {
        var items = (values || []).filter(function (v) { return v; });
        if (!items.length) {
            return '<span class="text-muted">—</span>';
        }
        return items.map(function (item) {
            return '<div>' + escapeHtml(item) + '</div>';
        }).join('');
    }

    function renderReport(data) {
        $('#report-period').text(
            'Period: ' + formatDate(data.fromDate) + ' to ' + formatDate(data.toDate) +
            ' — ' + (data.vendorMonthCount || 0) + ' vendor-month(s)'
        );

        var $tbody = $('#report-lines');
        $tbody.empty();

        var months = data.months || data.Months || [];
        if (!months.length) {
            $tbody.append('<tr><td colspan="7" class="text-muted text-center">No purchases found.</td></tr>');
            $('#report-footer').addClass('d-none');
            return;
        }

        months.forEach(function (month) {
            var monthLabel = readValue(month, 'monthLabel', 'MonthLabel') || '';
            var vendorCount = readValue(month, 'vendorCount', 'VendorCount') || 0;
            $tbody.append(
                '<tr class="table-light">' +
                '<th colspan="2">' + escapeHtml(monthLabel) +
                ' <span class="fw-normal text-muted">(' + vendorCount + ' vendor(s))</span></th>' +
                '<th class="text-end">' + formatAmount(readValue(month, 'totalExValue', 'TotalExValue')) + '</th>' +
                '<th class="text-end">' + formatAmount(readValue(month, 'totalTax', 'TotalTax')) + '</th>' +
                '<th class="text-end">' + formatAmount(readValue(month, 'totalNet', 'TotalNet')) + '</th>' +
                '<th class="text-end">' + formatAmount(readValue(month, 'totalPayments', 'TotalPayments')) + '</th>' +
                '<th></th>' +
                '</tr>'
            );

            (readValue(month, 'lines', 'Lines') || []).forEach(function (line) {
                $tbody.append(
                    '<tr>' +
                    '<td>' + escapeHtml(readValue(line, 'vendorName', 'VendorName') || '') + '</td>' +
                    '<td>' + renderRefList(readValue(line, 'billRefs', 'BillRefs')) + '</td>' +
                    '<td class="text-end">' + formatAmount(readValue(line, 'exValue', 'ExValue')) + '</td>' +
                    '<td class="text-end">' + formatAmount(readValue(line, 'taxAmount', 'TaxAmount')) + '</td>' +
                    '<td class="text-end fw-semibold">' + formatAmount(readValue(line, 'netAmount', 'NetAmount')) + '</td>' +
                    '<td class="text-end fw-semibold">' + formatAmount(readValue(line, 'paymentAmount', 'PaymentAmount')) + '</td>' +
                    '<td>' + renderRefList(readValue(line, 'paidAgainstBills', 'PaidAgainstBills')) + '</td>' +
                    '</tr>'
                );
            });
        });

        $('#report-total-exvalue').text(formatAmount(data.totalExValue));
        $('#report-total-tax').text(formatAmount(data.totalTax));
        $('#report-total-net').text(formatAmount(data.totalNet));
        $('#report-total-payments').text(formatAmount(data.totalPayments));
        $('#report-footer').removeClass('d-none');
    }

    function loadVendors() {
        if (window.initPaAjaxSelect2) {
            window.initPaAjaxSelect2($('#filter-vendor'), {
                entity: 'vendor',
                placeholder: 'Type to search vendor',
                allowClear: true
            });
            return $.Deferred().resolve().promise();
        }

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

        $.getJSON('/api/purchase-reports/monthly-by-vendor', params)
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
