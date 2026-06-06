(function () {
    'use strict';

    var itemModal = null;
    var dataTable = null;
    var canCreate = false;
    var canEdit = false;
    var canDelete = false;

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function showFormError(message) {
        $('#item-form-error').removeClass('d-none').text(message);
    }

    function clearFormError() {
        $('#item-form-error').addClass('d-none').text('');
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        if (!body) {
            return fallback;
        }
        return body.message || body.Message || fallback;
    }

    function showCompanyWarning(message) {
        $('#item-company-warning')
            .removeClass('d-none')
            .text(message || 'Select a company from the top navbar to manage items.');
    }

    function hideCompanyWarning() {
        $('#item-company-warning').addClass('d-none').text('');
    }

    function toggleStockFields(isEdit) {
        $('#opening-stock-group').toggleClass('d-none', isEdit);
        $('#current-stock-group').toggleClass('d-none', !isEdit);
    }

    function loadLookups() {
        return $.when(
            $.getJSON('/api/items/categories'),
            $.getJSON('/api/lookup/units-of-measure')
        ).then(function (categoriesRes, uomRes) {
            var $category = $('#item-category-id');
            $category.find('option:not(:first)').remove();
            (categoriesRes[0] || []).forEach(function (c) {
                $category.append($('<option></option>').val(c.id).text(c.name));
            });

            var $uom = $('#unit-of-measure-id');
            $uom.find('option:not(:first)').remove();
            (uomRes[0] || []).forEach(function (u) {
                var label = u.code ? u.name + ' (' + u.code + ')' : u.name;
                $uom.append($('<option></option>').val(u.id).text(label));
            });
        });
    }

    function initDataTable() {
        if (dataTable) {
            dataTable.ajax.reload();
            return;
        }

        dataTable = $('#items-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: {
                url: '/api/items/datatable',
                error: function (xhr) {
                    if (xhr.status === 400) {
                        showCompanyWarning(getApiErrorMessage(xhr, 'Select a company first.'));
                    }
                }
            },
            order: [[1, 'asc']],
            pageLength: 25,
            columns: [
                {
                    data: 'itemCode',
                    render: function (d) { return '<code>' + escapeHtml(d) + '</code>'; }
                },
                { data: 'itemName' },
                { data: 'itemType' },
                { data: 'categoryName', defaultContent: '—' },
                { data: 'unitSymbol' },
                {
                    data: 'saleRate',
                    className: 'text-end text-currency',
                    render: function (d) { return formatCurrency(d); }
                },
                {
                    data: 'currentStock',
                    className: 'text-end',
                    render: function (d) { return parseFloat(d).toFixed(2); }
                },
                {
                    data: 'isActive',
                    render: function (d) {
                        return d
                            ? '<span class="badge bg-success">Active</span>'
                            : '<span class="badge bg-secondary">Inactive</span>';
                    }
                },
                {
                    data: 'id',
                    orderable: false,
                    className: 'text-end',
                    render: function (id) {
                        var actions = '';
                        if (canEdit) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 me-1 btn-edit-item" data-id="' + id + '" title="Edit"><i class="fa-solid fa-pen"></i></button>';
                        }
                        if (canDelete) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 text-danger btn-delete-item" data-id="' + id + '" title="Delete"><i class="fa-solid fa-trash"></i></button>';
                        }
                        return actions || '—';
                    }
                }
            ],
            language: { emptyTable: 'No items found.' }
        });
    }

    function resetItemForm() {
        $('#item-id').val('');
        $('#item-code, #item-name, #hs-code, #stack-no, #lot-no, #barcode, #item-description').val('');
        $('#item-type').val('1');
        $('#costing-method').val('1');
        $('#item-category-id, #unit-of-measure-id').val('').trigger('change');
        $('#purchase-rate, #sale-rate, #minimum-stock, #reorder-level, #opening-stock').val('0');
        $('#current-stock-display').val('');
        $('#item-active').prop('checked', true);
        toggleStockFields(false);
    }

    function openCreateModal() {
        clearFormError();
        resetItemForm();
        $('#itemModalLabel').text('New Item');
        $.getJSON('/api/items/next-item-code')
            .done(function (res) { $('#item-code').val(res.itemCode); })
            .fail(function (xhr) { showFormError(getApiErrorMessage(xhr, 'Could not generate item code.')); });
        itemModal.show();
    }

    function openEditModal(id) {
        clearFormError();
        $.getJSON('/api/items/' + id)
            .done(function (item) {
                $('#itemModalLabel').text('Edit Item');
                $('#item-id').val(item.id);
                $('#item-code').val(item.itemCode);
                $('#item-name').val(item.itemName);
                $('#item-type').val(item.itemType);
                $('#costing-method').val(item.costingMethod);
                $('#item-category-id').val(item.itemCategoryId || '').trigger('change');
                $('#unit-of-measure-id').val(item.unitOfMeasureId).trigger('change');
                $('#hs-code').val(item.hsCode || '');
                $('#stack-no').val(item.stackNo || '');
                $('#lot-no').val(item.lotNo || '');
                $('#barcode').val(item.barcode || '');
                $('#purchase-rate').val(item.purchaseRate);
                $('#sale-rate').val(item.saleRate);
                $('#minimum-stock').val(item.minimumStock);
                $('#reorder-level').val(item.reorderLevel);
                $('#current-stock-display').val(parseFloat(item.currentStock).toFixed(2));
                $('#item-description').val(item.description || '');
                $('#item-active').prop('checked', item.isActive);
                toggleStockFields(true);
                itemModal.show();
            })
            .fail(function () { alert('Failed to load item.'); });
    }

    function buildPayload() {
        var categoryId = parseInt($('#item-category-id').val(), 10) || 0;
        return {
            id: parseInt($('#item-id').val(), 10) || null,
            itemType: parseInt($('#item-type').val(), 10) || 1,
            itemCode: $('#item-code').val().trim(),
            itemName: $('#item-name').val().trim(),
            stackNo: $('#stack-no').val().trim(),
            lotNo: $('#lot-no').val().trim(),
            description: $('#item-description').val().trim() || null,
            hsCode: $('#hs-code').val().trim() || null,
            barcode: $('#barcode').val().trim() || null,
            unitOfMeasureId: parseInt($('#unit-of-measure-id').val(), 10) || 0,
            itemCategoryId: categoryId > 0 ? categoryId : null,
            purchaseRate: parseFloat($('#purchase-rate').val()) || 0,
            saleRate: parseFloat($('#sale-rate').val()) || 0,
            minimumStock: parseFloat($('#minimum-stock').val()) || 0,
            reorderLevel: parseFloat($('#reorder-level').val()) || 0,
            openingStock: parseFloat($('#opening-stock').val()) || 0,
            costingMethod: parseInt($('#costing-method').val(), 10) || 1,
            isActive: $('#item-active').is(':checked')
        };
    }

    function saveItem(e) {
        e.preventDefault();
        clearFormError();

        var payload = buildPayload();
        var id = payload.id;
        $.ajax({
            url: id ? '/api/items/' + id : '/api/items',
            method: id ? 'PUT' : 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
            .done(function () {
                itemModal.hide();
                dataTable.ajax.reload(null, false);
            })
            .fail(function (xhr) {
                var body = xhr.responseJSON;
                showFormError(body && body.message ? body.message : getApiErrorMessage(xhr, 'Could not save item.'));
            });
    }

    function deleteItem(id) {
        if (!confirm('Delete this item?')) {
            return;
        }

        $.ajax({ url: '/api/items/' + id, method: 'DELETE' })
            .done(function () { dataTable.ajax.reload(null, false); })
            .fail(function (xhr) {
                var body = xhr.responseJSON;
                alert(body && body.message ? body.message : 'Could not delete item.');
            });
    }

    $(function () {
        var $perms = $('#item-permissions');
        canCreate = $perms.data('can-create') === true;
        canEdit = $perms.data('can-edit') === true;
        canDelete = $perms.data('can-delete') === true;

        if (!canCreate) {
            $('#btn-add-item').remove();
        }

        itemModal = new bootstrap.Modal(document.getElementById('itemModal'));

        $('#item-category-id, #unit-of-measure-id').select2({
            theme: 'bootstrap-5',
            width: '100%',
            dropdownParent: $('#itemModal')
        });

        $.getJSON('/api/company/current')
            .done(function () {
                hideCompanyWarning();
                loadLookups().always(initDataTable);
            })
            .fail(function () { showCompanyWarning(); });

        $('#btn-add-item').on('click', openCreateModal);
        $('#btn-generate-item-code').on('click', function () {
            $.getJSON('/api/items/next-item-code')
                .done(function (res) { $('#item-code').val(res.itemCode); });
        });
        $('#item-form').on('submit', saveItem);
        $('#items-table').on('click', '.btn-edit-item', function () { openEditModal($(this).data('id')); });
        $('#items-table').on('click', '.btn-delete-item', function () { deleteItem($(this).data('id')); });
    });
})();
