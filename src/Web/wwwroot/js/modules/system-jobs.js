(function () {
    'use strict';

    var backupsTable = null;
    var exportsTable = null;
    var canEdit = false;

    function getError(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        return (body && (body.message || body.Message)) || fallback;
    }

    function formatDate(value) {
        if (!value) return '—';
        var date = new Date(value);
        return isNaN(date.getTime()) ? '—' : date.toLocaleString();
    }

    function formatSize(bytes) {
        var size = Number(bytes || 0);
        if (!size) return '0 B';
        var units = ['B', 'KB', 'MB', 'GB'];
        var unit = 0;
        while (size >= 1024 && unit < units.length - 1) {
            size = size / 1024;
            unit++;
        }
        return size.toFixed(unit === 0 ? 0 : 2) + ' ' + units[unit];
    }

    function statusBadge(status) {
        var value = status;
        if (typeof value === 'number') {
            value = value === 2 ? 'completed' : value === 3 ? 'failed' : 'running';
        } else {
            value = (value || '').toString().toLowerCase();
        }
        if (value === 'completed') return '<span class="badge bg-success">Completed</span>';
        if (value === 'failed') return '<span class="badge bg-danger">Failed</span>';
        return '<span class="badge bg-warning text-dark">Running</span>';
    }

    function loadBackupTable() {
        if (backupsTable) {
            backupsTable.ajax.reload();
            return;
        }

        backupsTable = $('#backups-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: '/api/system-jobs/backups/datatable',
            order: [[4, 'desc']],
            pageLength: 25,
            columns: [
                { data: 'fileName' },
                { data: 'fileSizeBytes', render: function (d) { return formatSize(d); } },
                { data: 'runType' },
                { data: 'status', render: function (d) { return statusBadge(d); } },
                { data: 'startedAt', render: function (d) { return formatDate(d); } },
                { data: 'completedAt', render: function (d) { return formatDate(d); } },
                { data: 'createdBy', render: function (d) { return d || 'system'; } },
                {
                    data: 'id',
                    orderable: false,
                    className: 'text-end',
                    render: function (id, type, row) {
                        var html = '<a class="btn btn-link btn-sm p-0 me-2" href="/api/system-jobs/backups/download/' + id + '"><i class="fa-solid fa-download"></i></a>';
                        if (canEdit) {
                            html += '<button type="button" class="btn btn-link btn-sm p-0 text-danger btn-delete-backup" data-id="' + id + '"><i class="fa-solid fa-trash"></i></button>';
                        }
                        if (row.errorMessage) {
                            html += '<div class="small text-danger mt-1">' + row.errorMessage + '</div>';
                        }
                        return html;
                    }
                }
            ]
        });
    }

    function loadExportsTable() {
        if (exportsTable) {
            exportsTable.ajax.reload();
            return;
        }

        exportsTable = $('#exports-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: {
                url: '/api/system-jobs/exports/datatable',
                error: function (xhr) {
                    if (xhr.status === 400) {
                        $('#system-jobs-company-warning')
                            .removeClass('d-none')
                            .text(getError(xhr, 'Select a company first.'));
                    }
                }
            },
            order: [[4, 'desc']],
            pageLength: 25,
            columns: [
                { data: 'exportType' },
                { data: 'fileName' },
                { data: 'fileSizeBytes', render: function (d) { return formatSize(d); } },
                { data: 'status', render: function (d) { return statusBadge(d); } },
                { data: 'startedAt', render: function (d) { return formatDate(d); } },
                { data: 'completedAt', render: function (d) { return formatDate(d); } },
                { data: 'createdBy', render: function (d) { return d || 'system'; } },
                {
                    data: 'id',
                    orderable: false,
                    className: 'text-end',
                    render: function (id, type, row) {
                        var html = '<a class="btn btn-link btn-sm p-0 me-2" href="/api/system-jobs/exports/download/' + id + '"><i class="fa-solid fa-download"></i></a>';
                        if (canEdit) {
                            html += '<button type="button" class="btn btn-link btn-sm p-0 text-danger btn-delete-export" data-id="' + id + '"><i class="fa-solid fa-trash"></i></button>';
                        }
                        if (row.errorMessage) {
                            html += '<div class="small text-danger mt-1">' + row.errorMessage + '</div>';
                        }
                        return html;
                    }
                }
            ]
        });
    }

    function loadExportTypes() {
        return $.getJSON('/api/system-jobs/exports/types')
            .done(function (types) {
                var $select = $('#export-type-select');
                $select.empty();
                (types || []).forEach(function (item) {
                    $select.append($('<option></option>').val(item.code).text(item.name));
                });
            });
    }

    function runBackup(destination) {
        var $confirm = $('#btn-confirm-backup');
        var $run = $('#btn-run-backup');
        $confirm.prop('disabled', true);
        $run.prop('disabled', true);

        $.ajax({
            url: '/api/system-jobs/backups/run',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ destination: destination || 'Online' })
        })
            .done(function (result) {
                if (backupsTable) backupsTable.ajax.reload(null, false);

                if (result.shouldDownload && result.id) {
                    window.location.href = '/api/system-jobs/backups/download/' + result.id;
                }

                alert(result.message || 'Backup completed.');
            })
            .fail(function (xhr) {
                alert(getError(xhr, 'Backup failed.'));
                if (backupsTable) backupsTable.ajax.reload(null, false);
            })
            .always(function () {
                $confirm.prop('disabled', false);
                $run.prop('disabled', false);
                var modalEl = document.getElementById('backupDestinationModal');
                if (modalEl && window.bootstrap) {
                    bootstrap.Modal.getOrCreateInstance(modalEl).hide();
                }
            });
    }

    function openBackupDialog() {
        $('#backup-destination-online').prop('checked', true);
        var modalEl = document.getElementById('backupDestinationModal');
        if (modalEl && window.bootstrap) {
            bootstrap.Modal.getOrCreateInstance(modalEl).show();
        }
    }

    function confirmBackup() {
        var destination = $('input[name="backup-destination"]:checked').val() || 'Online';
        runBackup(destination);
    }

    function deleteBackup(id) {
        if (!confirm('Delete this backup record and file?')) return;
        $.ajax({ url: '/api/system-jobs/backups/delete/' + id, method: 'DELETE' })
            .done(function () { backupsTable.ajax.reload(null, false); })
            .fail(function (xhr) { alert(getError(xhr, 'Could not delete backup.')); });
    }

    function runExport() {
        var type = $('#export-type-select').val();
        if (!type) {
            alert('Select export type first.');
            return;
        }

        $.post('/api/system-jobs/exports/run/' + encodeURIComponent(type))
            .done(function (result) {
                alert(result.message || 'Export completed.');
                if (exportsTable) exportsTable.ajax.reload(null, false);
            })
            .fail(function (xhr) {
                alert(getError(xhr, 'Export failed.'));
                if (exportsTable) exportsTable.ajax.reload(null, false);
            });
    }

    function deleteExport(id) {
        if (!confirm('Delete this export record and file?')) return;
        $.ajax({ url: '/api/system-jobs/exports/delete/' + id, method: 'DELETE' })
            .done(function () { exportsTable.ajax.reload(null, false); })
            .fail(function (xhr) { alert(getError(xhr, 'Could not delete export.')); });
    }

    $(function () {
        canEdit = $('#system-jobs-permissions').data('can-edit') === true;

        if (!canEdit) {
            $('#btn-run-backup').remove();
            $('#btn-run-export').remove();
        }

        loadBackupTable();
        loadExportTypes();

        $.getJSON('/api/company/current')
            .done(function () {
                $('#system-jobs-company-warning').addClass('d-none');
                loadExportsTable();
            })
            .fail(function () {
                $('#system-jobs-company-warning')
                    .removeClass('d-none')
                    .text('Select a company from the top navbar.');
            });

        $('#btn-run-backup').on('click', openBackupDialog);
        $('#btn-confirm-backup').on('click', confirmBackup);
        $('#btn-run-export').on('click', runExport);
        $('#backups-table').on('click', '.btn-delete-backup', function () { deleteBackup($(this).data('id')); });
        $('#exports-table').on('click', '.btn-delete-export', function () { deleteExport($(this).data('id')); });
    });
})();
