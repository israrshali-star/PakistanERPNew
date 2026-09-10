(function () {
    'use strict';

    var customerModal = null;
    var dataTable = null;
    var canCreate = false;
    var canEdit = false;
    var canDelete = false;
    var currentCompanyId = 0;
    var HYPHENATED_TAX_ID_COMPANY_IDS = [2, 4, 5, 6, 7];

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function showFormError(message) {
        $('#customer-form-error').removeClass('d-none').text(message);
    }

    function clearFormError() {
        $('#customer-form-error').addClass('d-none').text('');
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        if (!body) {
            return fallback;
        }

        return body.message || body.Message || body.title || body.detail || fallback;
    }

    // API returns enums as strings (JsonStringEnumConverter); selects use numeric values.
    function normalizeInvoiceTypeValue(value) {
        if (value === null || value === undefined || value === '') {
            return 1;
        }

        var asNumber = parseInt(value, 10);
        if (!isNaN(asNumber) && asNumber > 0) {
            return asNumber;
        }

        var name = String(value).replace(/\s+/g, '').toLowerCase();
        if (name === 'salesinvoice' || name === 'saleinvoice') {
            return 1;
        }
        if (name === 'debitnote') {
            return 2;
        }
        if (name === 'creditnote') {
            return 3;
        }

        return 1;
    }

    function normalizeCustomerTypeValue(value) {
        if (value === null || value === undefined || value === '') {
            return 1;
        }

        var asNumber = parseInt(value, 10);
        if (!isNaN(asNumber) && asNumber > 0) {
            return asNumber;
        }

        var name = String(value).replace(/\s+/g, '').toLowerCase();
        if (name === 'unregistered') {
            return 2;
        }

        return 1;
    }

    function usesHyphenatedTaxIds() {
        return HYPHENATED_TAX_ID_COMPANY_IDS.indexOf(currentCompanyId) !== -1;
    }

    function digitsOnly(value) {
        return String(value || '').replace(/\D/g, '');
    }

    function formatCnicValue(value) {
        var digits = digitsOnly(value).slice(0, 13);
        if (!digits) {
            return '';
        }
        if (digits.length <= 5) {
            return digits;
        }
        if (digits.length <= 12) {
            return digits.slice(0, 5) + '-' + digits.slice(5);
        }
        return digits.slice(0, 5) + '-' + digits.slice(5, 12) + '-' + digits.slice(12);
    }

    function formatNtnValue(value) {
        var raw = String(value || '').trim().toUpperCase().replace(/\s+/g, '').replace(/\.-/g, '-');
        if (!raw) {
            return '';
        }

        if (/^\d{7}-\d$/.test(raw) || /^[A-Z]\d{6,7}-\d$/.test(raw)) {
            return raw;
        }

        if (/^[A-Z]/.test(raw)) {
            var letter = raw.charAt(0);
            var letterDigits = raw.slice(1).replace(/\D/g, '').slice(0, 8);
            if (letterDigits.length <= 6) {
                return letter + letterDigits;
            }
            if (letterDigits.length === 7) {
                return letter + letterDigits.slice(0, 6) + '-' + letterDigits.slice(6);
            }
            return letter + letterDigits.slice(0, 7) + '-' + letterDigits.slice(7);
        }

        var digits = digitsOnly(raw).slice(0, 8);
        if (digits.length <= 7) {
            return digits;
        }
        return digits.slice(0, 7) + '-' + digits.slice(7);
    }

    function applyTaxIdFormatsToForm() {
        if (!usesHyphenatedTaxIds()) {
            return;
        }

        $('#ntn').val(formatNtnValue($('#ntn').val()));
        $('#cnic').val(formatCnicValue($('#cnic').val()));
    }

    function syncTaxIdFormatHints() {
        var enabled = usesHyphenatedTaxIds();
        $('#ntn-format-hint, #cnic-format-hint').toggleClass('d-none', !enabled);
        $('#ntn').attr('placeholder', enabled ? '#######-#' : '');
        $('#cnic').attr('placeholder', enabled ? '#####-#######-#' : '');
    }

    function getSelectIntValue(selector) {
        var raw = $(selector).val();
        if (Array.isArray(raw)) {
            raw = raw[0];
        }
        return parseInt(raw, 10) || 0;
    }

    function selectDefaultScenario() {
        var $scenario = $('#scenario-id');
        var firstValue = $scenario.find('option:not([value=""])').first().val();

        if (firstValue) {
            $scenario.val(firstValue).trigger('change');
        }
    }

    function resetCustomerForm() {
        $('#customer-id').val('');
        $('#buyer-id').val('');
        $('#buyer-name').val('');
        $('#buyer-name-urdu').val('');
        $('#customer-type').val('1');
        $('#invoice-type').val('1');
        $('#opening-balance').val('0');
        $('#further-tax-rate').val('');
        $('#ntn, #cnic, #strn, #phone, #mobile, #email, #address').val('');
        $('#customer-active').prop('checked', true);
        $('#province-id').val('').trigger('change');
        selectDefaultScenario();
    }

    function showCompanyWarning(message) {
        $('#customer-company-warning')
            .removeClass('d-none')
            .text(message || 'Select a company from the top navbar to manage customers.');
    }

    function hideCompanyWarning() {
        $('#customer-company-warning').addClass('d-none').text('');
    }

    function ensureCompanySelected() {
        return $.getJSON('/api/company/current');
    }

    function loadLookups() {
        return $.when(
            $.getJSON('/api/lookup/provinces'),
            $.getJSON('/api/lookup/scenario-types')
        ).then(function (provincesRes, scenariosRes) {
            var provinces = provincesRes[0];
            var scenarios = scenariosRes[0];

            var $province = $('#province-id');
            $province.find('option:not(:first)').remove();
            provinces.forEach(function (p) {
                $province.append($('<option></option>').val(p.id).text(p.name));
            });

            var $scenario = $('#scenario-id');
            $scenario.empty();
            scenarios.forEach(function (s) {
                $scenario.append(
                    $('<option></option>').val(s.id).text(s.code + ' — ' + (s.description || ''))
                );
            });

            if (scenarios.length === 0) {
                $scenario.append('<option value="">No scenarios found</option>');
            } else if (!$('#customer-id').val()) {
                selectDefaultScenario();
            }

            if ($.fn.select2) {
                $('#province-id, #scenario-id').select2({ theme: 'bootstrap-5', width: '100%', dropdownParent: $('#customerModal') });
            }
        })
            .fail(function () {
                showFormError('Failed to load provinces or FBR scenarios.');
            });
    }

    function initDataTable() {
        if (dataTable) {
            return;
        }

        dataTable = $('#customers-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: {
                url: '/api/customers/datatable',
                type: 'GET',
                error: function (xhr) {
                    alert(getApiErrorMessage(xhr, 'Failed to load customers. Select a company from the top navbar and refresh.'));
                }
            },
            columns: [
                { data: 'buyerId', render: function (d) { return '<code>' + escapeHtml(d) + '</code>'; } },
                { data: 'buyerName' },
                { data: 'customerType' },
                { data: 'provinceName', defaultContent: '—' },
                { data: 'ntn', defaultContent: '—' },
                { data: 'phone', defaultContent: '—' },
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
                            '<a href="/Customers/Ledger/' + id + '" class="btn btn-link btn-sm p-0 me-1" title="Ledger"><i class="fa-solid fa-book"></i></a>' +
                            '<a href="/Customers/Statement/' + id + '" class="btn btn-link btn-sm p-0 me-1" title="Statement"><i class="fa-solid fa-file-lines"></i></a>';

                        if (canEdit) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 me-1 btn-edit-customer" data-id="' + id + '" title="Edit"><i class="fa-solid fa-pen"></i></button>';
                        }
                        if (canDelete) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 text-danger btn-delete-customer" data-id="' + id + '" title="Delete"><i class="fa-solid fa-trash"></i></button>';
                        }

                        return actions;
                    }
                }
            ],
            order: [[1, 'asc']],
            pageLength: 25,
            language: { emptyTable: 'No customers found.' }
        });
    }

    function openCreateModal() {
        clearFormError();
        $('#customerModalLabel').text('New Customer');
        resetCustomerForm();
        generateBuyerId();
        customerModal.show();
    }

    function openEditModal(id) {
        clearFormError();
        $.getJSON('/api/customers/' + id)
            .done(function (c) {
                $('#customerModalLabel').text('Edit Customer');
                $('#customer-id').val(c.id);
                $('#buyer-id').val(c.buyerId);
                $('#buyer-name').val(c.buyerName);
                $('#buyer-name-urdu').val(c.buyerNameUrdu || '');
                $('#customer-type').val(String(normalizeCustomerTypeValue(c.customerType)));
                $('#invoice-type').val(String(normalizeInvoiceTypeValue(c.invoiceType)));
                $('#scenario-id').val(c.scenarioId).trigger('change');
                $('#province-id').val(c.provinceId || '').trigger('change');
                $('#opening-balance').val(c.openingBalance);
                $('#further-tax-rate').val(c.furtherTaxRate != null ? c.furtherTaxRate : '');
                $('#ntn').val(c.ntn || '');
                $('#cnic').val(c.cnic || '');
                applyTaxIdFormatsToForm();
                $('#strn').val(c.strn || '');
                $('#phone').val(c.phone || '');
                $('#mobile').val(c.mobile || '');
                $('#email').val(c.email || '');
                $('#address').val(c.address || '');
                $('#customer-active').prop('checked', c.isActive);
                customerModal.show();
            })
            .fail(function () {
                alert('Failed to load customer.');
            });
    }

    function generateBuyerId() {
        return $.getJSON('/api/customers/next-buyer-id')
            .done(function (result) {
                $('#buyer-id').val(result.buyerId);
            })
            .fail(function (xhr) {
                showFormError(getApiErrorMessage(xhr, 'Could not generate buyer ID. Select a company from the top navbar.'));
            });
    }

    function saveCustomer(e) {
        e.preventDefault();
        clearFormError();

        var id = $('#customer-id').val();
        var provinceVal = $('#province-id').val();
        var scenarioId = getSelectIntValue('#scenario-id');

        if (!scenarioId) {
            showFormError('Please select an FBR scenario.');
            return;
        }

        if (!$('#buyer-id').val().trim() || !$('#buyer-name').val().trim()) {
            showFormError('Buyer ID and name are required.');
            return;
        }

        var payload = {
            id: id ? parseInt(id, 10) : null,
            buyerId: $('#buyer-id').val().trim(),
            buyerName: $('#buyer-name').val().trim(),
            buyerNameUrdu: $('#buyer-name-urdu').val().trim() || null,
            openingBalance: parseFloat($('#opening-balance').val()) || 0,
            address: $('#address').val().trim() || null,
            provinceId: provinceVal ? parseInt(provinceVal, 10) : null,
            scenarioId: scenarioId,
            phone: $('#phone').val().trim() || null,
            mobile: $('#mobile').val().trim() || null,
            email: $('#email').val().trim() || null,
            ntn: (usesHyphenatedTaxIds() ? formatNtnValue($('#ntn').val()) : $('#ntn').val().trim()) || null,
            cnic: (usesHyphenatedTaxIds() ? formatCnicValue($('#cnic').val()) : $('#cnic').val().trim()) || null,
            strn: $('#strn').val().trim() || null,
            customerType: normalizeCustomerTypeValue($('#customer-type').val()),
            invoiceType: normalizeInvoiceTypeValue($('#invoice-type').val()),
            furtherTaxRate: $('#further-tax-rate').val() === '' ? null : parseFloat($('#further-tax-rate').val()),
            isActive: $('#customer-active').is(':checked')
        };

        var request = id
            ? $.ajax({
                url: '/api/customers/' + id,
                method: 'PUT',
                contentType: 'application/json',
                data: JSON.stringify(payload)
            })
            : $.ajax({
                url: '/api/customers',
                method: 'POST',
                contentType: 'application/json',
                data: JSON.stringify(payload)
            });

        var $saveBtn = $('#btn-save-customer');
        $saveBtn.prop('disabled', true);

        request
            .done(function () {
                customerModal.hide();
                if (dataTable) {
                    dataTable.ajax.reload(null, false);
                } else {
                    window.location.reload();
                }
            })
            .fail(function (xhr) {
                showFormError(getApiErrorMessage(xhr, 'Failed to save customer.'));
            })
            .always(function () {
                $saveBtn.prop('disabled', false);
            });
    }

    function deleteCustomer(id) {
        if (!confirm('Delete this customer?')) {
            return;
        }

        $.ajax({ url: '/api/customers/' + id, method: 'DELETE' })
            .done(function () {
                dataTable.ajax.reload(null, false);
            })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Failed to delete customer.'));
            });
    }

    $(function () {
        var $perms = $('#customer-permissions');
        canCreate = $perms.attr('data-can-create') === 'true';
        canEdit = $perms.attr('data-can-edit') === 'true';
        canDelete = $perms.attr('data-can-delete') === 'true';

        customerModal = new bootstrap.Modal(document.getElementById('customerModal'));

        loadLookups().always(function () {
            ensureCompanySelected()
                .done(function (company) {
                    currentCompanyId = parseInt(company && (company.id || company.Id), 10) || 0;
                    syncTaxIdFormatHints();
                    hideCompanyWarning();
                    initDataTable();
                })
                .fail(function () {
                    showCompanyWarning();
                });
        });

        $('#ntn, #cnic').on('blur', function () {
            applyTaxIdFormatsToForm();
        });

        var $addBtn = $('#btn-add-customer');
        if ($addBtn.length) {
            $addBtn.on('click', function () {
                ensureCompanySelected()
                    .done(openCreateModal)
                    .fail(function () {
                        showCompanyWarning('Select a company from the top navbar before adding a customer.');
                    });
            });
        }
        $('#btn-generate-buyer-id').on('click', generateBuyerId);
        $('#customer-form').on('submit', saveCustomer);

        $('#customers-table').on('click', '.btn-edit-customer', function () {
            openEditModal($(this).data('id'));
        });

        $('#customers-table').on('click', '.btn-delete-customer', function () {
            deleteCustomer($(this).data('id'));
        });
    });
})();
