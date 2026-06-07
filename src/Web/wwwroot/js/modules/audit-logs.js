(function () {
    'use strict';

    var dataTable = null;

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function formatDate(dateValue) {
        if (!dateValue) {
            return '—';
        }

        var date = new Date(dateValue);
        return isNaN(date.getTime()) ? '—' : date.toLocaleString();
    }

    function initDataTable() {
        if (dataTable) {
            dataTable.ajax.reload();
            return;
        }

        dataTable = $('#audit-logs-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: {
                url: '/api/audit-logs/datatable',
                error: function (xhr) {
                    if (xhr.status === 400) {
                        $('#audit-log-company-warning')
                            .removeClass('d-none')
                            .text((xhr.responseJSON && xhr.responseJSON.message) || 'Select a company first.');
                    }
                }
            },
            order: [[0, 'desc']],
            pageLength: 25,
            columns: [
                { data: 'createdAt', render: function (d) { return formatDate(d); } },
                { data: 'action' },
                { data: 'tableName', render: function (d) { return d ? escapeHtml(d) : '—'; } },
                { data: 'recordId', render: function (d) { return d ? escapeHtml(d) : '—'; } },
                { data: 'userName', render: function (d) { return d ? escapeHtml(d) : 'System'; } },
                { data: 'companyName', render: function (d) { return d ? escapeHtml(d) : 'Global'; } },
                {
                    data: 'id',
                    orderable: false,
                    className: 'text-end',
                    render: function (id) {
                        return '<a href="/AuditLogs/Details/' + id + '" class="btn btn-link btn-sm p-0"><i class="fa-solid fa-eye me-1"></i>View</a>';
                    }
                }
            ],
            language: { emptyTable: 'No audit logs found.' }
        });
    }

    $(function () {
        $.getJSON('/api/company/current')
            .done(function () {
                $('#audit-log-company-warning').addClass('d-none');
                initDataTable();
            })
            .fail(function () {
                $('#audit-log-company-warning')
                    .removeClass('d-none')
                    .text('Select a company from the top navbar.');
            });
    });
})();
