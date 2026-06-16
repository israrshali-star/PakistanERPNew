(function () {
    'use strict';

    var dataTable = null;
    var canEdit = false;
    var canDelete = false;
    var bulkPrintEnabled = false;
    var bulkPrintItems = [];

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        if (!body) {
            return fallback;
        }
        return body.message || body.Message || fallback;
    }

    function formatInvoiceDate(value) {
        var dt = new Date(value);
        if (Number.isNaN(dt.getTime())) {
            return value;
        }
        return String(dt.getDate()).padStart(2, '0') + '/' +
            String(dt.getMonth() + 1).padStart(2, '0') + '/' +
            dt.getFullYear();
    }

    function updateBulkPrintButton() {
        var selectedCount = $('#bulk-print-lines .bulk-invoice-check:checked').length;
        $('#btn-bulk-print-pdf').prop('disabled', selectedCount === 0);
        $('#bulk-print-summary').text(selectedCount > 0
            ? selectedCount + ' invoice(s) selected for PDF.'
            : '');
    }

    function renderBulkPrintRows(items) {
        bulkPrintItems = items || [];
        var $tbody = $('#bulk-print-lines');
        $tbody.empty();
        $('#bulk-select-all').prop('checked', false);

        if (!bulkPrintItems.length) {
            $tbody.append('<tr><td colspan="6" class="text-muted text-center small">No submitted invoices match your search.</td></tr>');
            updateBulkPrintButton();
            return;
        }

        bulkPrintItems.forEach(function (item) {
            $tbody.append(
                '<tr>' +
                '<td><input type="checkbox" class="form-check-input bulk-invoice-check" value="' + item.id + '" /></td>' +
                '<td><code>' + escapeHtml(item.invoiceNumber) + '</code></td>' +
                '<td>' + escapeHtml(item.customerName) + '</td>' +
                '<td>' + formatInvoiceDate(item.invoiceDate) + '</td>' +
                '<td class="text-end text-currency">' + formatCurrency(item.netTotal) + '</td>' +
                '<td>' + (item.fbrInvoiceNumber ? '<code>' + escapeHtml(item.fbrInvoiceNumber) + '</code>' : '—') + '</td>' +
                '</tr>'
            );
        });

        updateBulkPrintButton();
    }

    function loadBulkPrintInvoices() {
        if (!bulkPrintEnabled) {
            return;
        }

        $.getJSON('/api/sales-invoices/submitted-for-print', {
            buyerName: $('#bulk-buyer-name').val(),
            invoiceNumber: $('#bulk-invoice-number').val(),
            fromDate: $('#filter-from').val(),
            toDate: $('#filter-to').val()
        })
            .done(renderBulkPrintRows)
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Failed to load submitted invoices.'));
            });
    }

    function printSelectedBulkPdf() {
        var ids = $('#bulk-print-lines .bulk-invoice-check:checked').map(function () {
            return parseInt($(this).val(), 10);
        }).get();

        if (!ids.length) {
            alert('Select at least one invoice to print.');
            return;
        }

        var $btn = $('#btn-bulk-print-pdf');
        $btn.prop('disabled', true);

        fetch('/api/sales-invoices/bulk-pdf', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ invoiceIds: ids })
        })
            .then(function (response) {
                if (!response.ok) {
                    return response.json().then(function (body) {
                        throw new Error(body.message || body.Message || 'Could not generate PDF.');
                    });
                }

                var disposition = response.headers.get('Content-Disposition') || '';
                var match = /filename="?([^";]+)"?/i.exec(disposition);
                var fileName = match ? match[1] : 'invoices.pdf';

                return response.blob().then(function (blob) {
                    return { blob: blob, fileName: fileName };
                });
            })
            .then(function (result) {
                var url = window.URL.createObjectURL(result.blob);
                var link = document.createElement('a');
                link.href = url;
                link.download = result.fileName;
                document.body.appendChild(link);
                link.click();
                link.remove();
                window.URL.revokeObjectURL(url);
            })
            .catch(function (err) {
                alert(err.message || 'Could not generate PDF.');
            })
            .finally(function () {
                updateBulkPrintButton();
            });
    }

    function initBulkPrintPanel(company) {
        bulkPrintEnabled = [2, 4, 5, 6, 7].indexOf(company.id) >= 0;
        if (!bulkPrintEnabled) {
            $('#bulk-print-panel').addClass('d-none');
            return;
        }

        $('#bulk-print-panel').removeClass('d-none');
        loadBulkPrintInvoices();
    }

    function runInvoiceAction(id, action, confirmText, successReload, method) {
        if (!confirm(confirmText)) {
            return;
        }

        $.ajax({
            url: '/api/sales-invoices/' + id + (action || ''),
            method: method || 'POST'
        })
            .done(function () {
                if (successReload && dataTable) {
                    dataTable.ajax.reload(null, false);
                }
            })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Action failed.'));
            });
    }

    function toInputDate(date) {
        return date.getFullYear() + '-' +
            String(date.getMonth() + 1).padStart(2, '0') + '-' +
            String(date.getDate()).padStart(2, '0');
    }

    function initDefaultDateFilters() {
        var today = new Date();
        var todayStr = toInputDate(today);
        $('#filter-from').val(todayStr);
        $('#filter-to').val(todayStr);
    }

    function reloadDataTable() {
        if (dataTable) {
            dataTable.ajax.reload();
        }
    }

    function initDataTable() {
        if (dataTable) {
            return;
        }

        dataTable = $('#sales-invoices-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: {
                url: '/api/sales-invoices/datatable',
                type: 'GET',
                data: function (d) {
                    d.fromDate = $('#filter-from').val();
                    d.toDate = $('#filter-to').val();
                },
                error: function (xhr) {
                    alert(getApiErrorMessage(xhr, 'Failed to load invoices. Select a company from the top navbar.'));
                }
            },
            columns: [
                {
                    data: 'invoiceNumber',
                    render: function (d, type, row) {
                        return '<a href="/SalesInvoices/Details/' + row.id + '"><code>' + escapeHtml(d) + '</code></a>';
                    }
                },
                {
                    data: 'customerName',
                    render: function (d, type, row) {
                        var name = escapeHtml(d);
                        if (!row.customerId) {
                            return name;
                        }
                        return '<a href="/Customers/Ledger/' + row.customerId + '" class="text-decoration-none" title="Customer ledger">' +
                            name + ' <i class="fa-solid fa-book text-primary ms-1" style="font-size:0.85em"></i></a>';
                    }
                },
                {
                    data: 'invoiceDate',
                    render: function (d) {
                        return formatInvoiceDate(d);
                    }
                },
                {
                    data: 'netTotal',
                    className: 'text-end text-currency',
                    render: function (d) { return formatCurrency(d); }
                },
                {
                    data: 'status',
                    render: function (d) {
                        var cls = d === 'Posted' ? 'bg-success' : d === 'Cancelled' ? 'bg-danger' : 'bg-secondary';
                        return '<span class="badge ' + cls + '">' + escapeHtml(d) + '</span>';
                    }
                },
                {
                    data: 'fbrInvoiceNumber',
                    defaultContent: '—',
                    render: function (d, type, row) {
                        if (!d) {
                            return '—';
                        }
                        var html = '<code>' + escapeHtml(d) + '</code>';
                        if (row.canShareInvoice) {
                            html += ' <button type="button" class="btn btn-link btn-sm p-0 ms-1 btn-download-pdf" data-id="' + row.id + '" title="Download PDF"><i class="fa-solid fa-file-pdf text-danger"></i></button>';
                        }
                        return html;
                    }
                },
                {
                    data: 'id',
                    orderable: false,
                    className: 'text-end',
                    render: function (id, type, row) {
                        var actions =
                            '<a href="/SalesInvoices/Details/' + id + '" class="btn btn-link btn-sm p-0 me-1" title="View"><i class="fa-solid fa-eye"></i></a>';

                        if (row.customerId) {
                            actions += '<a href="/Customers/Ledger/' + row.customerId + '" class="btn btn-link btn-sm p-0 me-1" title="Customer ledger"><i class="fa-solid fa-book"></i></a>';
                        }

                        if (canEdit && row.canEdit) {
                            actions += '<a href="/SalesInvoices/Edit/' + id + '" class="btn btn-link btn-sm p-0 me-1" title="Edit draft"><i class="fa-solid fa-pen"></i></a>';
                        }
                        if (canEdit && row.canPost) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 me-1 text-success btn-post-invoice" data-id="' + id + '" title="Post to GL"><i class="fa-solid fa-book"></i></button>';
                        }
                        if (canEdit && row.canSubmitFbr) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 me-1 text-primary btn-submit-fbr" data-id="' + id + '" title="Submit to FBR"><i class="fa-solid fa-paper-plane"></i></button>';
                        }
                        if (row.canShareInvoice) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 me-1 text-danger btn-download-pdf" data-id="' + id + '" title="Download PDF"><i class="fa-solid fa-file-pdf"></i></button>';
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 me-1 text-success btn-share-invoice" data-id="' + id + '" title="Email or WhatsApp"><i class="fa-solid fa-share-nodes"></i></button>';
                        }
                        if (canEdit && row.status === 'Draft') {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 text-danger btn-cancel-invoice" data-id="' + id + '" title="Cancel"><i class="fa-solid fa-ban"></i></button>';
                        }
                        if (canDelete && row.canDelete) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 text-danger btn-delete-invoice" data-id="' + id + '" title="Delete"><i class="fa-solid fa-trash"></i></button>';
                        }

                        return actions;
                    }
                }
            ],
            order: [[2, 'asc']],
            pageLength: 25,
            language: { emptyTable: 'No sales invoices yet.' }
        });
    }

    $(function () {
        canEdit = $('#sales-invoice-permissions').data('can-edit') === true;
        canDelete = $('#sales-invoice-permissions').data('can-delete') === true;

        initDefaultDateFilters();
        $('#btn-apply-filter').on('click', function () {
            reloadDataTable();
            loadBulkPrintInvoices();
        });
        $('#filter-from, #filter-to').on('change', function () {
            reloadDataTable();
            loadBulkPrintInvoices();
        });

        $('#btn-bulk-search').on('click', loadBulkPrintInvoices);
        $('#bulk-buyer-name, #bulk-invoice-number').on('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                loadBulkPrintInvoices();
            }
        });
        $('#bulk-select-all').on('change', function () {
            var checked = $(this).is(':checked');
            $('#bulk-print-lines .bulk-invoice-check').prop('checked', checked);
            updateBulkPrintButton();
        });
        $('#bulk-print-lines').on('change', '.bulk-invoice-check', updateBulkPrintButton);
        $('#btn-bulk-print-pdf').on('click', printSelectedBulkPdf);

        $.getJSON('/api/company/current')
            .done(function (company) {
                initDataTable();
                initBulkPrintPanel(company);
            })
            .fail(function () {
                $('#sales-company-warning')
                    .removeClass('d-none')
                    .text('Select a company from the top navbar to view sales invoices.');
            });

        $('#sales-invoices-table').on('click', '.btn-post-invoice', function () {
            runInvoiceAction($(this).data('id'), '/post', 'Post this invoice to the general ledger?', true);
        });

        $('#sales-invoices-table').on('click', '.btn-submit-fbr', function () {
            var id = $(this).data('id');
            if (window.SalesInvoiceFbr) {
                window.SalesInvoiceFbr.showFbrPreviewAndSubmit(id, function () {
                    if (dataTable) {
                        dataTable.ajax.reload(null, false);
                    }
                    if (window.SalesInvoiceShare) {
                        window.SalesInvoiceShare.openShareModal(id);
                    }
                });
                return;
            }
            runInvoiceAction(id, '/submit-fbr', 'Submit this invoice to FBR?', true);
        });

        $('#sales-invoices-table').on('click', '.btn-download-pdf', function (e) {
            e.preventDefault();
            e.stopPropagation();
            var id = $(this).data('id');
            if (window.SalesInvoiceFbr) {
                window.SalesInvoiceFbr.downloadInvoicePdf(id);
            }
        });

        $('#sales-invoices-table').on('click', '.btn-share-invoice', function (e) {
            e.preventDefault();
            e.stopPropagation();
            var id = $(this).data('id');
            if (window.SalesInvoiceShare) {
                window.SalesInvoiceShare.openShareModal(id);
            }
        });

        $('#sales-invoices-table').on('click', '.btn-cancel-invoice', function () {
            runInvoiceAction($(this).data('id'), '/cancel', 'Cancel this draft invoice?', true);
        });

        $('#sales-invoices-table').on('click', '.btn-delete-invoice', function () {
            runInvoiceAction(
                $(this).data('id'),
                '',
                'Permanently delete this invoice and its GL journal entry? This cannot be undone.',
                true,
                'DELETE'
            );
        });
    });
})();
