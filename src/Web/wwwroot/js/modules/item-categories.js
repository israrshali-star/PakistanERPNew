(function () {
    'use strict';

    var categoryModal = null;
    var dataTable = null;
    var canCreate = false;
    var canEdit = false;
    var canDelete = false;

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function showFormError(message) {
        $('#category-form-error').removeClass('d-none').text(message);
    }

    function clearFormError() {
        $('#category-form-error').addClass('d-none').text('');
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        return body && (body.message || body.Message) ? (body.message || body.Message) : fallback;
    }

    function showCompanyWarning(message) {
        $('#category-company-warning')
            .removeClass('d-none')
            .text(message || 'Select a company from the top navbar.');
    }

    function hideCompanyWarning() {
        $('#category-company-warning').addClass('d-none').text('');
    }

    function initDataTable() {
        if (dataTable) {
            dataTable.ajax.reload();
            return;
        }

        dataTable = $('#item-categories-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: {
                url: '/api/item-categories/datatable',
                error: function (xhr) {
                    if (xhr.status === 400) {
                        showCompanyWarning(getApiErrorMessage(xhr, 'Select a company first.'));
                    }
                }
            },
            order: [[0, 'asc']],
            pageLength: 25,
            columns: [
                { data: 'name' },
                {
                    data: 'description',
                    render: function (d) { return d ? escapeHtml(d) : '—'; }
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
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 me-1 btn-edit-category" data-id="' + id + '"><i class="fa-solid fa-pen"></i></button>';
                        }
                        if (canDelete) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 text-danger btn-delete-category" data-id="' + id + '"><i class="fa-solid fa-trash"></i></button>';
                        }
                        return actions || '—';
                    }
                }
            ],
            language: { emptyTable: 'No categories found.' }
        });
    }

    function openCreateModal() {
        clearFormError();
        $('#categoryModalLabel').text('New Category');
        $('#category-id').val('');
        $('#category-form')[0].reset();
        categoryModal.show();
    }

    function openEditModal(id) {
        clearFormError();
        $.getJSON('/api/item-categories/' + id)
            .done(function (c) {
                $('#categoryModalLabel').text('Edit Category');
                $('#category-id').val(c.id);
                $('#category-name').val(c.name);
                $('#category-description').val(c.description || '');
                categoryModal.show();
            })
            .fail(function () { alert('Failed to load category.'); });
    }

    function saveCategory(e) {
        e.preventDefault();
        clearFormError();

        var id = $('#category-id').val();
        var payload = {
            id: id ? parseInt(id, 10) : null,
            name: $('#category-name').val().trim(),
            description: $('#category-description').val().trim() || null
        };

        $.ajax({
            url: id ? '/api/item-categories/' + id : '/api/item-categories',
            method: id ? 'PUT' : 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
            .done(function () {
                categoryModal.hide();
                dataTable.ajax.reload(null, false);
            })
            .fail(function (xhr) {
                showFormError(getApiErrorMessage(xhr, 'Could not save category.'));
            });
    }

    function deleteCategory(id) {
        if (!confirm('Delete this category?')) {
            return;
        }

        $.ajax({ url: '/api/item-categories/' + id, method: 'DELETE' })
            .done(function () { dataTable.ajax.reload(null, false); })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Could not delete category.'));
            });
    }

    $(function () {
        var $perms = $('#category-permissions');
        canCreate = $perms.data('can-create') === true;
        canEdit = $perms.data('can-edit') === true;
        canDelete = $perms.data('can-delete') === true;

        if (!canCreate) {
            $('#btn-add-category').remove();
        }

        categoryModal = new bootstrap.Modal(document.getElementById('categoryModal'));

        $.getJSON('/api/company/current')
            .done(function () {
                hideCompanyWarning();
                initDataTable();
            })
            .fail(function () { showCompanyWarning(); });

        $('#btn-add-category').on('click', openCreateModal);
        $('#category-form').on('submit', saveCategory);
        $('#item-categories-table').on('click', '.btn-edit-category', function () { openEditModal($(this).data('id')); });
        $('#item-categories-table').on('click', '.btn-delete-category', function () { deleteCategory($(this).data('id')); });
    });
})();
