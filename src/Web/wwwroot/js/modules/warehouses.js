(function () {
    'use strict';

    var warehouseModal = null;
    var dataTable = null;
    var canCreate = false;
    var canEdit = false;
    var canDelete = false;

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function showFormError(message) {
        $('#warehouse-form-error').removeClass('d-none').text(message);
    }

    function clearFormError() {
        $('#warehouse-form-error').addClass('d-none').text('');
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        return body && (body.message || body.Message) ? (body.message || body.Message) : fallback;
    }

    function initDataTable() {
        if (dataTable) {
            dataTable.ajax.reload();
            return;
        }

        dataTable = $('#warehouses-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: {
                url: '/api/warehouses/datatable',
                error: function (xhr) {
                    if (xhr.status === 400) {
                        $('#warehouse-company-warning')
                            .removeClass('d-none')
                            .text(getApiErrorMessage(xhr, 'Select a company first.'));
                    }
                }
            },
            order: [[1, 'asc']],
            pageLength: 25,
            columns: [
                { data: 'code', render: function (d) { return '<code>' + escapeHtml(d) + '</code>'; } },
                { data: 'name' },
                { data: 'address', defaultContent: '—', render: function (d) { return d ? escapeHtml(d) : '—'; } },
                {
                    data: 'isActive',
                    render: function (d) {
                        return d
                            ? '<span class="badge bg-success">Active</span>'
                            : '<span class="badge bg-secondary">Inactive</span>';
                    }
                },
                {
                    data: 'transactionCount',
                    className: 'text-end',
                    render: function (d) { return parseInt(d, 10) || 0; }
                },
                {
                    data: 'id',
                    orderable: false,
                    className: 'text-end',
                    render: function (id) {
                        var actions = '';
                        if (canEdit) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 me-1 btn-edit-warehouse" data-id="' + id + '"><i class="fa-solid fa-pen"></i></button>';
                        }
                        if (canDelete) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 text-danger btn-delete-warehouse" data-id="' + id + '"><i class="fa-solid fa-trash"></i></button>';
                        }
                        return actions || '—';
                    }
                }
            ],
            language: { emptyTable: 'No warehouses found.' }
        });
    }

    function openCreateModal() {
        clearFormError();
        $('#warehouseModalLabel').text('New Warehouse');
        $('#warehouse-id').val('');
        $('#warehouse-form')[0].reset();
        $('#warehouse-active').prop('checked', true);
        $.getJSON('/api/warehouses/next-code')
            .done(function (res) { $('#warehouse-code').val(res.code); });
        warehouseModal.show();
    }

    function openEditModal(id) {
        clearFormError();
        $.getJSON('/api/warehouses/' + id)
            .done(function (w) {
                $('#warehouseModalLabel').text('Edit Warehouse');
                $('#warehouse-id').val(w.id);
                $('#warehouse-code').val(w.code);
                $('#warehouse-name').val(w.name);
                $('#warehouse-address').val(w.address || '');
                $('#warehouse-active').prop('checked', w.isActive);
                warehouseModal.show();
            })
            .fail(function () { alert('Failed to load warehouse.'); });
    }

    function saveWarehouse(e) {
        e.preventDefault();
        clearFormError();

        var id = $('#warehouse-id').val();
        var payload = {
            id: id ? parseInt(id, 10) : null,
            code: $('#warehouse-code').val().trim(),
            name: $('#warehouse-name').val().trim(),
            address: $('#warehouse-address').val().trim() || null,
            isActive: $('#warehouse-active').is(':checked')
        };

        $.ajax({
            url: id ? '/api/warehouses/' + id : '/api/warehouses',
            method: id ? 'PUT' : 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
            .done(function () {
                warehouseModal.hide();
                dataTable.ajax.reload(null, false);
            })
            .fail(function (xhr) {
                showFormError(getApiErrorMessage(xhr, 'Could not save warehouse.'));
            });
    }

    function deleteWarehouse(id) {
        if (!confirm('Delete this warehouse?')) {
            return;
        }

        $.ajax({ url: '/api/warehouses/' + id, method: 'DELETE' })
            .done(function () { dataTable.ajax.reload(null, false); })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Could not delete warehouse.'));
            });
    }

    $(function () {
        var $perms = $('#warehouse-permissions');
        canCreate = $perms.data('can-create') === true;
        canEdit = $perms.data('can-edit') === true;
        canDelete = $perms.data('can-delete') === true;

        if (!canCreate) {
            $('#btn-add-warehouse').remove();
        }

        warehouseModal = new bootstrap.Modal(document.getElementById('warehouseModal'));

        $.getJSON('/api/company/current')
            .done(function () {
                $('#warehouse-company-warning').addClass('d-none');
                initDataTable();
            })
            .fail(function () {
                $('#warehouse-company-warning')
                    .removeClass('d-none')
                    .text('Select a company from the top navbar.');
            });

        $('#btn-add-warehouse').on('click', openCreateModal);
        $('#btn-generate-warehouse-code').on('click', function () {
            $.getJSON('/api/warehouses/next-code')
                .done(function (res) { $('#warehouse-code').val(res.code); });
        });
        $('#warehouse-form').on('submit', saveWarehouse);
        $('#warehouses-table').on('click', '.btn-edit-warehouse', function () { openEditModal($(this).data('id')); });
        $('#warehouses-table').on('click', '.btn-delete-warehouse', function () { deleteWarehouse($(this).data('id')); });
    });
})();
