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

    function buildRefCell(line) {
        var refs = readValue(line, 'appliedRefs', 'AppliedRefs') || [];
        var unallocated = parseFloat(readValue(line, 'unallocatedAmount', 'UnallocatedAmount')) || 0;
        var parts = [];

        refs.forEach(function (ref) {
            var refNo = readValue(ref, 'refNo', 'RefNo') || '—';
            var billDate = readValue(ref, 'billDate', 'BillDate');
            var dateBit = billDate ? ' <span class="text-muted">(' + formatDate(billDate) + ')</span>' : '';
            parts.push('<div>' + escapeHtml(refNo) + dateBit + '</div>');
        });

        if (unallocated > 0.004) {
            parts.push('<div class="text-muted">Advance / Unallocated</div>');
        }

        if (parts.length === 0) {
            return '<span class="text-muted">—</span>';
        }

        return parts.join('');
    }

    function buildAppliedCell(line) {
        var refs = readValue(line, 'appliedRefs', 'AppliedRefs') || [];
        var unallocated = parseFloat(readValue(line, 'unallocatedAmount', 'UnallocatedAmount')) || 0;
        var parts = [];

        refs.forEach(function (ref) {
            parts.push('<div class="text-end">' + formatAmount(readValue(ref, 'appliedAmount', 'AppliedAmount')) + '</div>');
        });

        if (unallocated > 0.004) {
            parts.push('<div class="text-end text-muted">' + formatAmount(unallocated) + '</div>');
        }

        if (parts.length === 0) {
            return '<span class="text-muted">—</span>';
        }

        return parts.join('');
    }

    var lastReport = null;

    function renderReport(data) {
        lastReport = data;
        var period = 'Period: ' + formatDate(data.fromDate) + ' to ' + formatDate(data.toDate) +
            ' — ' + (data.paymentCount || 0) + ' payment(s)';
        if (data.vendorLabel) {
            period += ' — ' + data.vendorLabel;
        }
        $('#report-period').text(period);

        var $tbody = $('#report-lines');
        $tbody.empty();

        var months = data.months || data.Months || [];
        if (!months.length) {
            $tbody.append('<tr><td colspan="8" class="text-muted text-center">No vendor payments found.</td></tr>');
            $('#report-footer').addClass('d-none');
            return;
        }

        months.forEach(function (month) {
            var monthLabel = readValue(month, 'monthLabel', 'MonthLabel') || '';
            var monthTotal = readValue(month, 'totalAmount', 'TotalAmount') || 0;
            var paymentCount = readValue(month, 'paymentCount', 'PaymentCount') || 0;

            $tbody.append(
                '<tr class="table-light">' +
                '<th colspan="5">' + escapeHtml(monthLabel) +
                ' <span class="fw-normal text-muted">(' + paymentCount + ')</span></th>' +
                '<th class="text-end">' + formatAmount(monthTotal) + '</th>' +
                '<th colspan="2"></th>' +
                '</tr>'
            );

            (readValue(month, 'lines', 'Lines') || []).forEach(function (line) {
                $tbody.append(
                    '<tr>' +
                    '<td>' + formatDate(readValue(line, 'paymentDate', 'PaymentDate')) + '</td>' +
                    '<td><code>' + escapeHtml(readValue(line, 'paymentNumber', 'PaymentNumber') || '') + '</code></td>' +
                    '<td>' + escapeHtml(readValue(line, 'vendorName', 'VendorName') || '') + '</td>' +
                    '<td>' + escapeHtml(readValue(line, 'source', 'Source') || '') + '</td>' +
                    '<td>' + escapeHtml(readValue(line, 'paymentMethod', 'PaymentMethod') || '') + '</td>' +
                    '<td class="text-end fw-semibold">' + formatAmount(readValue(line, 'amount', 'Amount')) + '</td>' +
                    '<td>' + buildRefCell(line) + '</td>' +
                    '<td>' + buildAppliedCell(line) + '</td>' +
                    '</tr>'
                );
            });
        });

        $('#report-total-amount').text(formatAmount(data.totalAmount));
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
            toDate: to
        };

        var vendorId = parseInt($('#filter-vendor').val(), 10);
        if (vendorId > 0) {
            params.vendorId = vendorId;
        }

        $.getJSON('/api/purchase-reports/vendor-payments-monthly', params)
            .done(renderReport)
            .fail(function (xhr) {
                lastReport = null;
                alert(getApiErrorMessage(xhr, 'Failed to load report.'));
            });
    }

    function formatShareMessage(data) {
        var total = formatAmount(data.totalAmount);
        var message = 'Vendor Payments (Monthly)\n' +
            formatDate(data.fromDate) + ' to ' + formatDate(data.toDate) + '\n' +
            (data.paymentCount || 0) + ' payment(s) — Total Rs ' + total + '/-';
        if (data.vendorLabel) {
            message += '\n' + data.vendorLabel;
        }
        return message;
    }

    function fetchReportPdf() {
        var from = $('#filter-from').val();
        var to = $('#filter-to').val();
        var url = '/api/purchase-reports/vendor-payments-monthly/pdf?fromDate=' +
            encodeURIComponent(from) + '&toDate=' + encodeURIComponent(to);
        var vendorId = parseInt($('#filter-vendor').val(), 10);
        if (vendorId > 0) {
            url += '&vendorId=' + vendorId;
        }

        return fetch(url).then(function (response) {
            if (!response.ok) {
                return response.json().then(function (body) {
                    throw new Error(body.message || body.Message || 'Could not create PDF.');
                }).catch(function (err) {
                    if (err instanceof Error && err.message !== 'Could not create PDF.') {
                        throw err;
                    }
                    throw new Error('Could not create PDF.');
                });
            }

            var disposition = response.headers.get('Content-Disposition') || '';
            var match = /filename\*?=(?:UTF-8''|")?([^";]+)/i.exec(disposition);
            var fileName = match ? decodeURIComponent(match[1].replace(/"/g, '')) : 'vendor-payments-monthly.pdf';
            return response.blob().then(function (blob) {
                return { blob: blob, fileName: fileName };
            });
        });
    }

    function shareReportOnWhatsApp() {
        if (!lastReport || !(lastReport.months || lastReport.Months || []).length) {
            alert('Load the report first, then send it on WhatsApp.');
            return;
        }

        var message = formatShareMessage(lastReport);
        var $btn = $('#btn-whatsapp-report');
        $btn.prop('disabled', true);

        fetchReportPdf()
            .then(function (pdf) {
                var file = new File([pdf.blob], pdf.fileName, { type: 'application/pdf' });
                if (navigator.canShare && navigator.canShare({ files: [file] })) {
                    return navigator.share({
                        files: [file],
                        title: pdf.fileName,
                        text: message
                    });
                }

                var link = document.createElement('a');
                link.href = window.URL.createObjectURL(pdf.blob);
                link.download = pdf.fileName;
                document.body.appendChild(link);
                link.click();
                link.remove();
                window.open('https://wa.me/?text=' + encodeURIComponent(message), '_blank');
            })
            .catch(function (err) {
                if (err && err.name === 'AbortError') {
                    return;
                }
                alert(err.message || 'Could not share the report on WhatsApp.');
            })
            .finally(function () {
                $btn.prop('disabled', false);
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
        $('#btn-whatsapp-report').on('click', shareReportOnWhatsApp);
    });
})();
