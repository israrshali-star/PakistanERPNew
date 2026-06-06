(function () {
    'use strict';

    var vendorModal = null;
    var dataTable = null;
    var canCreate = false;
    var canEdit = false;
    var canDelete = false;

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function showFormError(message) {
        $('#vendor-form-error').removeClass('d-none').text(message);
    }

    function clearFormError() {
        $('#vendor-form-error').addClass('d-none').text('');
    }

    function loadProvinces() {
        return $.getJSON('/api/lookup/provinces').then(function (provinces) {
            var $province = $('#province-id');
            $province.find('option:not(:first)').remove();
            provinces.forEach(function (p) {
                $province.append($('<option></option>').val(p.id).text(p.name));
            });

            if ($.fn.select2) {
                $('#province-id').select2({ theme: 'bootstrap-5', width: '100%', dropdownParent: $('#vendorModal') });
            }
        });
    }

    function initDataTable() {
        dataTable = $('#vendors-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: { url: '/api/vendors/datatable', type: 'GET' },
            columns: [
                { data: 'vendorCode', render: function (d) { return '<code>' + escapeHtml(d) + '</code>'; } },
                { data: 'vendorName' },
                { data: 'provinceName', defaultContent: '—' },
                { data: 'ntn', defaultContent: '—' },
                { data: 'phone', defaultContent: '—' },
                {
                    data: 'defaultSalesTaxRate',
                    className: 'text-end',
                    render: function (d) { return parseFloat(d).toFixed(2) + '%'; }
                },
                {
                    data: 'openingBalance',
                    className: 'text-end text-currency',
                    render: function (d) { return formatCurrency(d); }
                },
                {
                    data: 'balance',
                    className: 'text-end text-currency fw-semibold',
                    render: function (d) { return formatCurrency(d); }
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
                        var actions =
                            '<a href="/Vendors/Ledger/' + id + '" class="btn btn-link btn-sm p-0 me-1" title="Ledger"><i class="fa-solid fa-book"></i></a>' +
                            '<a href="/Vendors/Statement/' + id + '" class="btn btn-link btn-sm p-0 me-1" title="Statement"><i class="fa-solid fa-file-lines"></i></a>';

                        if (canEdit) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 me-1 btn-edit-vendor" data-id="' + id + '" title="Edit"><i class="fa-solid fa-pen"></i></button>';
                        }
                        if (canDelete) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 text-danger btn-delete-vendor" data-id="' + id + '" title="Delete"><i class="fa-solid fa-trash"></i></button>';
                        }

                        return actions;
                    }
                }
            ],
            order: [[1, 'asc']],
            pageLength: 25,
            language: { emptyTable: 'No vendors found.' }
        });
    }

    function openCreateModal() {
        clearFormError();
        $('#vendorModalLabel').text('New Vendor');
        $('#vendor-id').val('');
        $('#vendor-form')[0].reset();
        $('#vendor-active').prop('checked', true);
        $('#opening-balance').val('0');
        $('#default-tax-rate').val('18');
        $('#province-id').val('').trigger('change');
        generateVendorCode();
        vendorModal.show();
    }

    function openEditModal(id) {
        clearFormError();
        $.getJSON('/api/vendors/' + id)
            .done(function (v) {
                $('#vendorModalLabel').text('Edit Vendor');
                $('#vendor-id').val(v.id);
                $('#vendor-code').val(v.vendorCode);
                $('#vendor-name').val(v.vendorName);
                $('#province-id').val(v.provinceId || '').trigger('change');
                $('#default-tax-rate').val(v.defaultSalesTaxRate);
                $('#opening-balance').val(v.openingBalance);
                $('#ntn').val(v.ntn || '');
                $('#phone').val(v.phone || '');
                $('#email').val(v.email || '');
                $('#address').val(v.address || '');
                $('#vendor-active').prop('checked', v.isActive);
                vendorModal.show();
            })
            .fail(function () {
                alert('Failed to load vendor.');
            });
    }

    function generateVendorCode() {
        $.getJSON('/api/vendors/next-vendor-code')
            .done(function (result) {
                $('#vendor-code').val(result.vendorCode);
            });
    }

    function saveVendor(e) {
        e.preventDefault();
        clearFormError();

        var id = $('#vendor-id').val();
        var provinceVal = $('#province-id').val();
        var payload = {
            id: id ? parseInt(id, 10) : null,
            vendorCode: $('#vendor-code').val().trim(),
            vendorName: $('#vendor-name').val().trim(),
            openingBalance: parseFloat($('#opening-balance').val()) || 0,
            address: $('#address').val().trim() || null,
            provinceId: provinceVal ? parseInt(provinceVal, 10) : null,
            phone: $('#phone').val().trim() || null,
            email: $('#email').val().trim() || null,
            ntn: $('#ntn').val().trim() || null,
            defaultSalesTaxRate: parseFloat($('#default-tax-rate').val()) || 18,
            isActive: $('#vendor-active').is(':checked')
        };

        var request = id
            ? $.ajax({
                url: '/api/vendors/' + id,
                method: 'PUT',
                contentType: 'application/json',
                data: JSON.stringify(payload)
            })
            : $.ajax({
                url: '/api/vendors',
                method: 'POST',
                contentType: 'application/json',
                data: JSON.stringify(payload)
            });

        request
            .done(function () {
                vendorModal.hide();
                dataTable.ajax.reload(null, false);
            })
            .fail(function (xhr) {
                var message = 'Failed to save vendor.';
                if (xhr.responseJSON && xhr.responseJSON.message) {
                    message = xhr.responseJSON.message;
                }
                showFormError(message);
            });
    }

    function deleteVendor(id) {
        if (!confirm('Delete this vendor?')) {
            return;
        }

        $.ajax({ url: '/api/vendors/' + id, method: 'DELETE' })
            .done(function () {
                dataTable.ajax.reload(null, false);
            })
            .fail(function (xhr) {
                var message = 'Failed to delete vendor.';
                if (xhr.responseJSON && xhr.responseJSON.message) {
                    message = xhr.responseJSON.message;
                }
                alert(message);
            });
    }

    $(function () {
        var $perms = $('#vendor-permissions');
        canCreate = $perms.attr('data-can-create') === 'true';
        canEdit = $perms.attr('data-can-edit') === 'true';
        canDelete = $perms.attr('data-can-delete') === 'true';

        vendorModal = new bootstrap.Modal(document.getElementById('vendorModal'));

        loadProvinces().always(initDataTable);

        $('#btn-add-vendor').on('click', openCreateModal);
        $('#btn-generate-vendor-code').on('click', generateVendorCode);
        $('#vendor-form').on('submit', saveVendor);

        $('#vendors-table').on('click', '.btn-edit-vendor', function () {
            openEditModal($(this).data('id'));
        });

        $('#vendors-table').on('click', '.btn-delete-vendor', function () {
            deleteVendor($(this).data('id'));
        });
    });
})();
