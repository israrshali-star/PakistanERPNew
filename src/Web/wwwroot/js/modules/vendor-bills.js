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

    function runBillAction(id, action, confirmText, method) {
        if (!confirm(confirmText)) {
            return;
        }

        $.ajax({
            url: '/api/vendor-bills/' + id + action,
            method: method || 'POST'
        })
            .done(function () {
                if (dataTable) {
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

        dataTable = $('#vendor-bills-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: {
                url: '/api/vendor-bills/datatable',
                type: 'GET',
                error: function (xhr) {
                    alert(getApiErrorMessage(xhr, 'Failed to load bills. Select a company from the top navbar.'));
                }
            },
            columns: [
                {
                    data: 'billNumber',
                    render: function (d, type, row) {
                        return '<a href="/VendorBills/Details/' + row.id + '"><code>' + escapeHtml(d) + '</code></a>';
                    }
                },
                { data: 'vendorName' },
                {
                    data: 'billDate',
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
                    data: 'netAmount',
                    className: 'text-end text-currency',
                    render: function (d) { return formatCurrency(d); }
                },
                {
                    data: 'status',
                    render: function (d) {
                        var cls = d === 'Approved' ? 'bg-success' : d === 'Cancelled' ? 'bg-danger' : 'bg-secondary';
                        return '<span class="badge ' + cls + '">' + escapeHtml(d) + '</span>';
                    }
                },
                {
                    data: 'id',
                    orderable: false,
                    className: 'text-end',
                    render: function (id, type, row) {
                        var actions =
                            '<a href="/VendorBills/Details/' + id + '" class="btn btn-link btn-sm p-0 me-1" title="View"><i class="fa-solid fa-eye"></i></a>';

                        if (canEdit && row.canEdit) {
                            actions += '<a href="/VendorBills/Edit/' + id + '" class="btn btn-link btn-sm p-0 me-1" title="Edit draft"><i class="fa-solid fa-pen"></i></a>';
                        }
                        if (canEdit && row.canApprove) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 me-1 text-success btn-approve-bill" data-id="' + id + '" title="Approve & Post to GL"><i class="fa-solid fa-check"></i></button>';
                        }
                        if (canDelete && row.canDelete) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 text-danger btn-delete-bill" data-id="' + id + '" title="Delete"><i class="fa-solid fa-trash"></i></button>';
                        }

                        return actions;
                    }
                }
            ],
            order: [[2, 'desc']],
            pageLength: 25,
            language: { emptyTable: 'No vendor bills yet.' }
        });
    }

    $(function () {
        canEdit = $('#bill-permissions').data('can-edit') === true;
        canDelete = $('#bill-permissions').data('can-delete') === true;

        $.getJSON('/api/company/current')
            .done(initDataTable)
            .fail(function () {
                $('#bill-company-warning')
                    .removeClass('d-none')
                    .text('Select a company from the top navbar to view vendor bills.');
            });

        $('#vendor-bills-table').on('click', '.btn-approve-bill', function () {
            runBillAction($(this).data('id'), '/approve', 'Approve this bill and post to the general ledger?');
        });

        $('#vendor-bills-table').on('click', '.btn-delete-bill', function () {
            runBillAction($(this).data('id'), '', 'Delete this draft bill? It cannot be undone.', 'DELETE');
        });
    });
})();
