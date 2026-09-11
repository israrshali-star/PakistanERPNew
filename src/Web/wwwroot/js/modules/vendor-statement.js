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
        $('#stmt-vendor-name').text(data.vendor.vendorName);
        $('#stmt-vendor-code').text(data.vendor.vendorCode);

        if (data.vendor.ntn) {
            $('#stmt-ntn').text(data.vendor.ntn);
            $('#stmt-ntn-wrap').removeClass('d-none');
        } else {
            $('#stmt-ntn-wrap').addClass('d-none');
        }

        $('#stmt-period').text(
            'Period: ' + formatDate(data.fromDate) + ' to ' + formatDate(data.toDate)
        );

        var $tbody = $('#statement-entries');
        var canViewPurchase = $tbody.data('can-view-purchase') === true || $tbody.data('can-view-purchase') === 'true';
        var colSpan = canViewPurchase ? 8 : 7;
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

            var billId = entry.billId || entry.BillId || 0;
            var paymentId = entry.paymentId || entry.PaymentId || 0;
            var attachments = entry.attachments || entry.Attachments || [];
            var refHtml = '<code>' + $('<div>').text(entry.reference).html() + '</code>';
            if (billId && canViewPurchase) {
                refHtml = '<a href="/VendorBills/Details/' + billId + '" class="text-decoration-none" title="View vendor bill">' +
                    refHtml + '</a>';
            } else if (paymentId && canViewPurchase) {
                refHtml = '<a href="/api/vendor-payments/' + paymentId + '/pdf" class="text-decoration-none" target="_blank" title="View payment PDF">' +
                    refHtml + '</a>';
            }

            var docsHtml = '—';
            if (attachments.length) {
                docsHtml = attachments.map(function (doc) {
                    var id = doc.id || doc.Id;
                    var name = doc.fileName || doc.FileName || 'document';
                    var safeName = $('<div>').text(name).html();
                    return '<span class="d-inline-flex align-items-center gap-1 me-2">' +
                        '<a class="btn btn-link btn-sm p-0 text-primary" href="/api/vendor-bills/attachments/' + id +
                        '/download" target="_blank" title="View ' + safeName + '"><i class="fa-solid fa-eye"></i></a>' +
                        '<a class="btn btn-link btn-sm p-0 text-secondary" href="/api/vendor-bills/attachments/' + id +
                        '/download" title="Download ' + safeName + '"><i class="fa-solid fa-download"></i></a></span>';
                }).join(' ');
            }

            var actionsHtml = '';
            if (canViewPurchase) {
                if (billId) {
                    actionsHtml = '<td class="no-print text-end text-nowrap">' +
                        '<a href="/VendorBills/Details/' + billId + '" class="btn btn-link btn-sm p-0 me-1" title="View vendor bill">' +
                        '<i class="fa-solid fa-eye"></i></a>';
                    if (attachments.length) {
                        var first = attachments[0];
                        var firstId = first.id || first.Id;
                        var firstName = String(first.fileName || first.FileName || 'document').replace(/"/g, '');
                        actionsHtml += '<button type="button" class="btn btn-link btn-sm p-0 text-success js-share-file" data-url="/api/vendor-bills/attachments/' +
                            firstId + '/download" data-name="' + firstName + '" title="Share bill document">' +
                            '<i class="fa-solid fa-share-nodes"></i></button>';
                    }
                    actionsHtml += '</td>';
                } else if (paymentId) {
                    actionsHtml = '<td class="no-print text-end text-nowrap">' +
                        '<a href="/api/vendor-payments/' + paymentId + '/pdf" class="btn btn-link btn-sm p-0 me-1" target="_blank" title="View payment PDF">' +
                        '<i class="fa-solid fa-eye"></i></a>' +
                        '<button type="button" class="btn btn-link btn-sm p-0 text-success js-share-vendor-payment" data-id="' +
                        paymentId + '" title="Share payment"><i class="fa-brands fa-whatsapp"></i></button>' +
                        '</td>';
                } else {
                    actionsHtml = '<td class="no-print text-end"><span class="text-muted">—</span></td>';
                }
            }

            $tbody.append(
                '<tr>' +
                '<td>' + dateText + '</td>' +
                '<td>' + refHtml + '</td>' +
                '<td>' + $('<div>').text(entry.description).html() + '</td>' +
                '<td class="text-end">' + (entry.debit > 0 ? formatAmount(entry.debit) : '—') + '</td>' +
                '<td class="text-end">' + (entry.credit > 0 ? formatAmount(entry.credit) : '—') + '</td>' +
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
        var vendorId = $('#statement-vendor-id').val();
        var from = parseDateInput($('#statement-from').val());
        var to = parseDateInput($('#statement-to').val());

        if (!from || !to) {
            alert('Please select valid from and to dates.');
            return;
        }

        $.getJSON('/api/vendors/' + vendorId + '/statement', { from: from, to: to })
            .done(renderStatement)
            .fail(function (xhr) {
                var message = 'Failed to load statement.';
                if (xhr.responseJSON && xhr.responseJSON.message) {
                    message = xhr.responseJSON.message;
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
                    partyType: 'vendor',
                    partyId: parseInt($('#statement-vendor-id').val(), 10),
                    fromDate: from,
                    toDate: to
                });
            }
        });

        loadStatement();
    });
})();
