(function () {
    'use strict';

    var uomModal = null;
    var dataTable = null;
    var canCreate = false;
    var canEdit = false;
    var canDelete = false;

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function showFormError(message) {
        $('#uom-form-error').removeClass('d-none').text(message);
    }

    function clearFormError() {
        $('#uom-form-error').addClass('d-none').text('');
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

        dataTable = $('#uom-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: { url: '/api/units-of-measure/datatable' },
            order: [[0, 'asc']],
            pageLength: 25,
            columns: [
                { data: 'name' },
                {
                    data: 'symbol',
                    render: function (d) { return d ? '<code>' + escapeHtml(d) + '</code>' : '—'; }
                },
                {
                    data: 'itemCount',
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
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 me-1 btn-edit-uom" data-id="' + id + '"><i class="fa-solid fa-pen"></i></button>';
                        }
                        if (canDelete) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 text-danger btn-delete-uom" data-id="' + id + '"><i class="fa-solid fa-trash"></i></button>';
                        }
                        return actions || '—';
                    }
                }
            ],
            language: { emptyTable: 'No units found. Six default units are seeded on first run.' }
        });
    }

    function openCreateModal() {
        clearFormError();
        $('#uomModalLabel').text('New Unit of Measure');
        $('#uom-id').val('');
        $('#uom-form')[0].reset();
        uomModal.show();
    }

    function openEditModal(id) {
        clearFormError();
        $.getJSON('/api/units-of-measure/' + id)
            .done(function (u) {
                $('#uomModalLabel').text('Edit Unit of Measure');
                $('#uom-id').val(u.id);
                $('#uom-name').val(u.name);
                $('#uom-symbol').val(u.symbol || '');
                uomModal.show();
            })
            .fail(function () { alert('Failed to load unit.'); });
    }

    function saveUom(e) {
        e.preventDefault();
        clearFormError();

        var id = $('#uom-id').val();
        var payload = {
            id: id ? parseInt(id, 10) : null,
            name: $('#uom-name').val().trim(),
            symbol: $('#uom-symbol').val().trim() || null
        };

        $.ajax({
            url: id ? '/api/units-of-measure/' + id : '/api/units-of-measure',
            method: id ? 'PUT' : 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
            .done(function () {
                uomModal.hide();
                dataTable.ajax.reload(null, false);
            })
            .fail(function (xhr) {
                showFormError(getApiErrorMessage(xhr, 'Could not save unit.'));
            });
    }

    function deleteUom(id) {
        if (!confirm('Delete this unit of measure?')) {
            return;
        }

        $.ajax({ url: '/api/units-of-measure/' + id, method: 'DELETE' })
            .done(function () { dataTable.ajax.reload(null, false); })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Could not delete unit.'));
            });
    }

    $(function () {
        var $perms = $('#uom-permissions');
        canCreate = $perms.data('can-create') === true;
        canEdit = $perms.data('can-edit') === true;
        canDelete = $perms.data('can-delete') === true;

        if (!canCreate) {
            $('#btn-add-uom').remove();
        }

        uomModal = new bootstrap.Modal(document.getElementById('uomModal'));
        initDataTable();

        $('#btn-add-uom').on('click', openCreateModal);
        $('#uom-form').on('submit', saveUom);
        $('#uom-table').on('click', '.btn-edit-uom', function () { openEditModal($(this).data('id')); });
        $('#uom-table').on('click', '.btn-delete-uom', function () { deleteUom($(this).data('id')); });
    });
})();
