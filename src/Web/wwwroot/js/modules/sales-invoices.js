(function () {
    'use strict';

    var dataTable = null;
    var canEdit = false;
    var canDelete = false;

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
                        var dt = new Date(d);
                        if (Number.isNaN(dt.getTime())) {
                            return d;
                        }
                        return String(dt.getDate()).padStart(2, '0') + '/' +
                            String(dt.getMonth() + 1).padStart(2, '0') + '/' +
                            dt.getFullYear();
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
                        if (row.hasFbrPdf) {
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
            order: [[2, 'desc']],
            pageLength: 25,
            language: { emptyTable: 'No sales invoices yet.' }
        });
    }

    $(function () {
        canEdit = $('#sales-invoice-permissions').data('can-edit') === true;
        canDelete = $('#sales-invoice-permissions').data('can-delete') === true;

        $.getJSON('/api/company/current')
            .done(initDataTable)
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
