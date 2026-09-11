(function () {
    'use strict';

    function formatDate(value) {
        var d = new Date(value);
        if (Number.isNaN(d.getTime())) {
            return value;
        }
        var day = String(d.getDate()).padStart(2, '0');
        var month = String(d.getMonth() + 1).padStart(2, '0');
        var year = d.getFullYear();
        return day + '/' + month + '/' + year;
    }

    function parseDateInput(value) {
        if (!value) {
            return null;
        }
        var parts = value.split('/');
        if (parts.length !== 3) {
            return null;
        }
        return parts[2] + '-' + parts[1] + '-' + parts[0];
    }

    function formatAmount(value) {
        var num = parseFloat(value) || 0;
        return num.toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function renderStatement(data) {
        var $tbody = $('#statement-entries');
        var canViewInvoice = $tbody.data('can-view-invoice') === true || $tbody.data('can-view-invoice') === 'true';
        var canOpenReceipt = $tbody.data('can-open-receipt') === true || $tbody.data('can-open-receipt') === 'true';
        var canEditReceipt = $tbody.data('can-edit-receipt') === true || $tbody.data('can-edit-receipt') === 'true';
        var showRowActions = canViewInvoice || canOpenReceipt;
        var returnUrl = $tbody.attr('data-return-url') || '';
        var colSpan = showRowActions ? 9 : 8;

        $('#stmt-customer-name').text(data.customer.buyerName);
        $('#stmt-buyer-id').text(data.customer.buyerId);

        if (data.customer.ntn) {
            $('#stmt-ntn').text(data.customer.ntn);
            $('#stmt-ntn-wrap').removeClass('d-none');
        } else {
            $('#stmt-ntn-wrap').addClass('d-none');
        }

        $('#stmt-period').text(
            'Period: ' + formatDate(data.fromDate) + ' to ' + formatDate(data.toDate)
        );

        $tbody.empty();

        if (!data.entries || data.entries.length === 0) {
            $tbody.append('<tr><td colspan="' + colSpan + '" class="text-muted text-center">No transactions in this period.</td></tr>');
            $('#statement-footer').addClass('d-none');
            return;
        }

        data.entries.forEach(function (entry) {
            var dateText = entry.date && entry.date.indexOf('0001') === -1
                ? formatDate(entry.date)
                : '—';

            var pending = entry.pendingCredit > 0 ? formatAmount(entry.pendingCredit) : '—';
            var rowClass = entry.pendingCredit > 0 ? ' class="table-warning"' : '';
            var docsHtml = '—';
            var attachments = entry.attachments || entry.Attachments || [];
            if (attachments.length) {
                docsHtml = attachments.map(function (doc) {
                    var id = doc.id || doc.Id;
                    var name = doc.fileName || doc.FileName || 'document';
                    var safeName = $('<div>').text(name).html();
                    return '<span class="d-inline-flex align-items-center gap-1 me-2">' +
                        '<a class="btn btn-link btn-sm p-0 text-primary" href="/api/customers/receipt-attachments/' + id + '/download" target="_blank" title="View ' +
                        safeName + '"><i class="fa-solid fa-eye"></i></a>' +
                        '<a class="btn btn-link btn-sm p-0 text-secondary" href="/api/customers/receipt-attachments/' + id + '/download?download=1" title="Download ' +
                        safeName + '"><i class="fa-solid fa-download"></i></a></span>';
                }).join(' ');
            }

            var receiptId = entry.receiptId || entry.ReceiptId || 0;
            var invoiceId = entry.invoiceId || entry.InvoiceId || 0;
            var canShareInvoice = entry.canShareInvoice === true || entry.CanShareInvoice === true;
            var editHref = '/CustomerReceipts?edit=' + receiptId;
            if (returnUrl) {
                editHref += '&returnUrl=' + encodeURIComponent(returnUrl);
            }
            var refHtml = '<code>' + $('<div>').text(entry.reference).html() + '</code>';
            if (receiptId && canOpenReceipt) {
                refHtml = '<a href="' + editHref + '" class="text-decoration-none" title="' +
                    (canEditReceipt ? 'View / edit receipt' : 'View receipt') + '">' + refHtml + '</a>';
            } else if (invoiceId && canViewInvoice) {
                refHtml = '<a href="/SalesInvoices/Details/' + invoiceId + '" class="text-decoration-none" title="View invoice">' +
                    refHtml + '</a>';
            }

            var actionsHtml = '';
            if (showRowActions) {
                if (invoiceId && canViewInvoice) {
                    actionsHtml = '<td class="no-print text-end text-nowrap">' +
                        '<a href="/SalesInvoices/Details/' + invoiceId + '" class="btn btn-link btn-sm p-0 me-1" title="View invoice">' +
                        '<i class="fa-solid fa-eye"></i></a>';
                    if (canShareInvoice) {
                        actionsHtml += '<button type="button" class="btn btn-link btn-sm p-0 text-success js-share-invoice" data-id="' +
                            invoiceId + '" title="Share invoice"><i class="fa-solid fa-share-nodes"></i></button>';
                    }
                    actionsHtml += '</td>';
                } else if (receiptId && canOpenReceipt) {
                    actionsHtml = '<td class="no-print text-end text-nowrap">' +
                        '<a href="' + editHref + '" class="btn btn-link btn-sm p-0 me-1" title="' +
                        (canEditReceipt ? 'View / edit receipt' : 'View receipt') + '">' +
                        '<i class="fa-solid fa-eye"></i></a>' +
                        '<button type="button" class="btn btn-link btn-sm p-0 me-1 js-print-receipt" data-id="' + receiptId +
                        '" title="Print / PDF"><i class="fa-solid fa-print"></i></button>' +
                        '<button type="button" class="btn btn-link btn-sm p-0 text-success js-share-receipt" data-id="' +
                        receiptId + '" title="Share receipt"><i class="fa-brands fa-whatsapp"></i></button>' +
                        '</td>';
                } else {
                    actionsHtml = '<td class="no-print text-end"><span class="text-muted">—</span></td>';
                }
            }

            $tbody.append(
                '<tr' + rowClass + '>' +
                '<td>' + dateText + '</td>' +
                '<td>' + refHtml + '</td>' +
                '<td>' + $('<div>').text(entry.description).html() + '</td>' +
                '<td class="text-end">' + (entry.debit > 0 ? formatAmount(entry.debit) : '—') + '</td>' +
                '<td class="text-end">' + (entry.credit > 0 ? formatAmount(entry.credit) : '—') + '</td>' +
                '<td class="text-end text-muted">' + pending + '</td>' +
                '<td class="text-end fw-semibold">' + formatAmount(entry.balance) + '</td>' +
                '<td class="no-print">' + docsHtml + '</td>' +
                actionsHtml +
                '</tr>'
            );
        });

        $('#stmt-closing-balance').text(formatAmount(data.closingBalance));
        $('#statement-footer').removeClass('d-none');
    }

    function loadStatement() {
        var customerId = $('#statement-customer-id').val();
        var from = parseDateInput($('#statement-from').val());
        var to = parseDateInput($('#statement-to').val());

        if (!from || !to) {
            alert('Please select valid from and to dates.');
            return;
        }

        $.getJSON('/api/customers/' + customerId + '/statement', { from: from, to: to })
            .done(renderStatement)
            .fail(function (xhr) {
                var message = 'Failed to load statement.';
                var body = xhr && xhr.responseJSON;
                if (body) {
                    message = body.message || body.Message || message;
                }
                alert(message);
            });
    }

    $(function () {
        var today = new Date();
        var firstOfMonth = new Date(today.getFullYear(), today.getMonth(), 1);

        if (typeof flatpickr !== 'undefined') {
            flatpickr('#statement-from', {
                dateFormat: 'd/m/Y',
                defaultDate: firstOfMonth,
                allowInput: true
            });
            flatpickr('#statement-to', {
                dateFormat: 'd/m/Y',
                defaultDate: today,
                allowInput: true
            });
        }

        $('#btn-load-statement').on('click', loadStatement);
        $('#btn-print-statement').on('click', function () {
            window.print();
        });

        $('#btn-share-statement').on('click', function () {
            var from = parseDateInput($('#statement-from').val());
            var to = parseDateInput($('#statement-to').val());
            if (!from || !to) {
                alert('Please select valid from and to dates.');
                return;
            }
            if (window.LedgerShare) {
                window.LedgerShare.open({
                    partyType: 'customer',
                    partyId: parseInt($('#statement-customer-id').val(), 10),
                    fromDate: from,
                    toDate: to
                });
            }
        });

        loadStatement();
    });
})();
