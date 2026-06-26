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

    function runAction(id, method, urlSuffix, confirmText) {
        if (!confirm(confirmText)) {
            return;
        }

        $.ajax({
            url: '/api/journal-entries/' + id + urlSuffix,
            method: method
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

        dataTable = $('#journal-entries-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: {
                url: '/api/journal-entries/datatable',
                type: 'GET',
                error: function (xhr) {
                    alert(getApiErrorMessage(xhr, 'Failed to load journal entries. Select a company from the top navbar.'));
                }
            },
            columns: [
                {
                    data: 'entryNumber',
                    render: function (d, type, row) {
                        return '<a href="/JournalEntries/Details/' + row.id + '"><code>' + escapeHtml(d) + '</code></a>';
                    }
                },
                {
                    data: 'entryDate',
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
                    data: 'description',
                    render: function (d) {
                        return d ? escapeHtml(d) : '<span class="text-muted">—</span>';
                    }
                },
                { data: 'sourceLabel', render: function (d) { return escapeHtml(d); } },
                {
                    data: 'totalDebit',
                    className: 'text-end text-currency',
                    render: function (d) { return formatCurrency(d); }
                },
                {
                    data: 'status',
                    render: function (d) {
                        var cls = d === 'Posted' ? 'bg-success' : d === 'Reversed' ? 'bg-danger' : 'bg-secondary';
                        return '<span class="badge ' + cls + '">' + escapeHtml(d) + '</span>';
                    }
                },
                {
                    data: 'id',
                    orderable: false,
                    className: 'text-end',
                    render: function (id, type, row) {
                        var actions =
                            '<a href="/JournalEntries/Details/' + id + '" class="btn btn-link btn-sm p-0 me-1" title="View"><i class="fa-solid fa-eye"></i></a>';

                        if (canEdit && row.canEdit) {
                            actions += '<a href="/JournalEntries/Edit/' + id + '" class="btn btn-link btn-sm p-0 me-1" title="Edit"><i class="fa-solid fa-pen"></i></a>';
                        }
                        if (canEdit && row.canPost) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 me-1 text-success btn-post-entry" data-id="' + id + '" title="Post"><i class="fa-solid fa-check"></i></button>';
                        }
                        if (canDelete && rowCanDelete(row)) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 text-danger btn-delete-entry" data-id="' + id + '" data-status="' + escapeHtml(row.status) + '" title="Delete"><i class="fa-solid fa-trash"></i></button>';
                        }

                        return actions;
                    }
                }
            ],
            order: [[1, 'desc']],
            pageLength: 25,
            language: { emptyTable: 'No journal entries yet.' }
        });
    }

    function detectPermissions() {
        var $perms = $('#je-permissions');
        canEdit = $perms.attr('data-can-edit') === 'true';
        canDelete = $perms.attr('data-can-delete') === 'true';
    }

    function rowCanDelete(row) {
        return row.canDelete === true || row.CanDelete === true;
    }

    $(function () {
        detectPermissions();

        $.getJSON('/api/company/current')
            .done(initDataTable)
            .fail(function () {
                $('#je-company-warning')
                    .removeClass('d-none')
                    .text('Select a company from the top navbar to view journal entries.');
            });

        $('#journal-entries-table').on('click', '.btn-post-entry', function () {
            runAction($(this).data('id'), 'POST', '/post', 'Post this journal entry to the general ledger?');
        });

        $('#journal-entries-table').on('click', '.btn-delete-entry', function () {
            var $btn = $(this);
            var status = $btn.data('status') || '';
            var confirmText = status === 'Posted'
                ? 'Delete this posted journal entry? It will be removed from the general ledger.'
                : 'Delete this draft journal entry?';
            runAction($btn.data('id'), 'DELETE', '', confirmText);
        });
    });
})();

