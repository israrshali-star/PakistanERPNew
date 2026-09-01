(function () {
    'use strict';

    var lastReport = null;
    var companyName = '';

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function formatAmount(value, blankZero) {
        var num = parseFloat(value) || 0;
        if (blankZero && Math.abs(num) < 0.005) {
            return '';
        }
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

    function renderReport(data) {
        lastReport = data;
        var period = 'Period: ' + formatDate(data.fromDate) + ' to ' + formatDate(data.toDate) +
            ' — ' + (data.customerCount || 0) + ' customer(s) with balances';
        if (data.customerLabel) {
            period += ' — ' + data.customerLabel;
        }
        $('#report-period').text(period);

        var $tbody = $('#report-lines');
        $tbody.empty();

        var lines = data.lines || data.Lines || [];
        if (!lines.length) {
            $tbody.append('<tr><td colspan="8" class="text-muted text-center">No customer balances found.</td></tr>');
            $('#report-footer').addClass('d-none');
            return;
        }

        lines.forEach(function (line) {
            $tbody.append(
                '<tr>' +
                '<td><code>' + escapeHtml(readValue(line, 'customerCode', 'CustomerCode') || '') + '</code></td>' +
                '<td>' + escapeHtml(readValue(line, 'customerName', 'CustomerName') || '') + '</td>' +
                '<td class="text-end">' + formatAmount(readValue(line, 'openingDebit', 'OpeningDebit'), true) + '</td>' +
                '<td class="text-end">' + formatAmount(readValue(line, 'openingCredit', 'OpeningCredit'), true) + '</td>' +
                '<td class="text-end">' + formatAmount(readValue(line, 'periodDebit', 'PeriodDebit'), true) + '</td>' +
                '<td class="text-end">' + formatAmount(readValue(line, 'periodCredit', 'PeriodCredit'), true) + '</td>' +
                '<td class="text-end fw-semibold">' + formatAmount(readValue(line, 'closingDebit', 'ClosingDebit'), true) + '</td>' +
                '<td class="text-end fw-semibold">' + formatAmount(readValue(line, 'closingCredit', 'ClosingCredit'), true) + '</td>' +
                '</tr>'
            );
        });

        $('#report-total-opening-debit').text(formatAmount(readValue(data, 'totalOpeningDebit', 'TotalOpeningDebit')));
        $('#report-total-opening-credit').text(formatAmount(readValue(data, 'totalOpeningCredit', 'TotalOpeningCredit')));
        $('#report-total-period-debit').text(formatAmount(readValue(data, 'totalPeriodDebit', 'TotalPeriodDebit')));
        $('#report-total-period-credit').text(formatAmount(readValue(data, 'totalPeriodCredit', 'TotalPeriodCredit')));
        $('#report-total-closing-debit').text(formatAmount(readValue(data, 'totalClosingDebit', 'TotalClosingDebit')));
        $('#report-total-closing-credit').text(formatAmount(readValue(data, 'totalClosingCredit', 'TotalClosingCredit')));
        $('#report-footer').removeClass('d-none');
    }

    function loadCustomers() {
        if (window.initPaAjaxSelect2) {
            window.initPaAjaxSelect2($('#filter-customer'), {
                entity: 'customer',
                placeholder: 'Type to search customer',
                allowClear: true
            });
            return $.Deferred().resolve().promise();
        }

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

    function buildQueryParams() {
        var from = $('#filter-from').val();
        var to = $('#filter-to').val();
        var params = {
            fromDate: from,
            toDate: to
        };

        var customerId = parseInt($('#filter-customer').val(), 10);
        if (customerId > 0) {
            params.customerId = customerId;
        }

        return params;
    }

    function loadReport() {
        var params = buildQueryParams();
        if (!params.fromDate || !params.toDate) {
            alert('Please select from and to dates.');
            return;
        }

        $.getJSON('/api/sales-reports/customer-balances', params)
            .done(renderReport)
            .fail(function (xhr) {
                lastReport = null;
                alert(getApiErrorMessage(xhr, 'Failed to load report.'));
            });
    }

    function formatShareMessage(data) {
        var message = 'Customer Balances\n';
        if (companyName) {
            message += companyName + '\n';
        }
        message += formatDate(data.fromDate) + ' to ' + formatDate(data.toDate) + '\n' +
            (data.customerCount || 0) + ' customer(s)\n' +
            'Debit Rs ' + formatAmount(readValue(data, 'totalClosingDebit', 'TotalClosingDebit')) +
            ' | Credit Rs ' + formatAmount(readValue(data, 'totalClosingCredit', 'TotalClosingCredit'));
        if (data.customerLabel) {
            message += '\n' + data.customerLabel;
        }
        return message;
    }

    function fetchReportPdf() {
        var params = buildQueryParams();
        var url = '/api/sales-reports/customer-balances/pdf?fromDate=' +
            encodeURIComponent(params.fromDate) + '&toDate=' + encodeURIComponent(params.toDate);
        if (params.customerId) {
            url += '&customerId=' + params.customerId;
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
            var fileName = match ? decodeURIComponent(match[1].replace(/"/g, '')) : 'customer-balances.pdf';
            return response.blob().then(function (blob) {
                return { blob: blob, fileName: fileName };
            });
        });
    }

    function downloadPdf() {
        if (!lastReport) {
            alert('Load the report first, then download the PDF.');
            return;
        }

        var $btn = $('#btn-pdf-report');
        $btn.prop('disabled', true);

        fetchReportPdf()
            .then(function (pdf) {
                var link = document.createElement('a');
                link.href = window.URL.createObjectURL(pdf.blob);
                link.download = pdf.fileName;
                document.body.appendChild(link);
                link.click();
                link.remove();
            })
            .catch(function (err) {
                alert(err.message || 'Could not download the PDF.');
            })
            .finally(function () {
                $btn.prop('disabled', false);
            });
    }

    function shareReportOnWhatsApp() {
        var lines = lastReport && (lastReport.lines || lastReport.Lines);
        if (!lastReport || !lines || !lines.length) {
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
            .done(function (company) {
                companyName = (company && (company.companyName || company.CompanyName)) || '';
                if (companyName) {
                    $('#report-company-name').text(companyName);
                }
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
        $('#btn-pdf-report').on('click', downloadPdf);
        $('#btn-whatsapp-report').on('click', shareReportOnWhatsApp);
    });
})();
