(function () {
    'use strict';

    var canCreate = false;
    var canEdit = false;
    var canDelete = false;
    var hasExistingToken = false;
    var companyModal = null;
    var provinces = [];

    function showError(message) {
        $('#settings-form-success').addClass('d-none');
        $('#settings-form-error').removeClass('d-none').text(message);
    }

    function showSuccess(message) {
        $('#settings-form-error').addClass('d-none');
        $('#settings-form-success').removeClass('d-none').text(message);
    }

    function clearMessages() {
        $('#settings-form-error, #settings-form-success').addClass('d-none').text('');
    }

    function showCompanyFormError(message) {
        $('#company-form-error').removeClass('d-none').text(message);
    }

    function clearCompanyFormError() {
        $('#company-form-error').addClass('d-none').text('');
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        if (!body) return fallback;
        return body.message || body.Message || fallback;
    }

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function updateFbrStatus(settings) {
        var $badge = $('#fbr-status-badge');
        var $hint = $('#fbr-mode-hint');

        if (settings.fbrLiveMode) {
            $badge.html('<span class="badge bg-success">FBR Live Mode</span>');
            $hint.removeClass('alert-info alert-warning').addClass('alert-success')
                .text('FBR URL and API token are configured. Posted invoices will submit to the live FBR API.');
        } else if (settings.fbrPostUrl || settings.hasApiToken) {
            $badge.html('<span class="badge bg-warning text-dark">FBR Partial Config</span>');
            $hint.removeClass('alert-info alert-success').addClass('alert-warning')
                .text('Both FBR Post URL and API token are required for live submission. Incomplete config uses simulation mode.');
        } else {
            $badge.html('<span class="badge bg-secondary">FBR Simulation</span>');
            $hint.removeClass('alert-warning alert-success').addClass('alert-info')
                .text('No FBR credentials configured. Invoice submission will run in simulation mode (FBR-DEMO-xxx numbers).');
        }

        hasExistingToken = settings.hasApiToken === true;
        $('#api-token-hint').text(
            hasExistingToken
                ? 'A token is stored. Enter a new value to replace it, or check "Clear" to remove.'
                : 'No API token stored yet.'
        );
    }

    function populateForm(settings) {
        $('#company-name').val(settings.companyName);
        $('#company-ntn').val(settings.ntn || '');
        $('#company-address').val(settings.address || '');
        $('#company-phone').val(settings.phone || '');
        $('#company-email').val(settings.email || '');
        $('#province-id').val(settings.provinceId || '');
        $('#fbr-post-url').val(settings.fbrPostUrl || '');
        $('#api-token').val('');
        $('#clear-api-token').prop('checked', false);
        $('#tax-group-name').val(settings.taxGroupName || 'Standard Rate');
        $('#sales-tax-rate').val(settings.salesTaxRate);
        $('#unreg-tax-rate').val(settings.unregisteredSalesTaxRate);
        updateFbrStatus(settings);
    }

    function populateProvinceSelects() {
        var selectors = ['#province-id', '#manage-province-id'];
        selectors.forEach(function (selector) {
            var $select = $(selector);
            var current = $select.val();
            $select.find('option:not(:first)').remove();
            provinces.forEach(function (p) {
                $select.append($('<option></option>').val(p.id).text(p.name));
            });
            if (current) {
                $select.val(current);
            }
        });
    }

    function loadProvinces() {
        return $.getJSON('/api/lookup/provinces').done(function (data) {
            provinces = data || [];
            populateProvinceSelects();
        });
    }

    function loadSettings() {
        return $.getJSON('/api/company-settings').done(populateForm);
    }

    function renderCompaniesTable(companies) {
        var $body = $('#companies-table-body');
        $body.empty();

        if (!companies || companies.length === 0) {
            $body.append('<tr><td colspan="6" class="text-muted text-center">No companies found.</td></tr>');
            return;
        }

        companies.forEach(function (c) {
            var contact = [];
            if (c.phone) contact.push(escapeHtml(c.phone));
            if (c.email) contact.push(escapeHtml(c.email));
            var contactText = contact.length ? contact.join('<br>') : '—';

            var defaultCell = '';
            if (c.isDefault) {
                defaultCell = '<span class="badge bg-primary">Default</span>';
            } else if (canEdit) {
                defaultCell = '<button type="button" class="btn btn-link btn-sm p-0 btn-set-default" data-id="' + c.id + '">Set default</button>';
            } else {
                defaultCell = '—';
            }

            var actions = '';
            if (canEdit) {
                actions += '<button type="button" class="btn btn-link btn-sm p-0 me-1 btn-edit-company" data-id="' + c.id + '" title="Edit"><i class="fa-solid fa-pen"></i></button>';
            }
            if (canDelete) {
                actions += '<button type="button" class="btn btn-link btn-sm p-0 text-danger btn-delete-company" data-id="' + c.id + '" title="Delete"><i class="fa-solid fa-trash"></i></button>';
            }
            if (!actions) {
                actions = '—';
            }

            $body.append(
                '<tr data-company-id="' + c.id + '">' +
                '<td class="fw-semibold">' + escapeHtml(c.companyName) + '</td>' +
                '<td>' + (c.ntn ? '<code>' + escapeHtml(c.ntn) + '</code>' : '—') + '</td>' +
                '<td>' + escapeHtml(c.provinceName || '—') + '</td>' +
                '<td class="small">' + contactText + '</td>' +
                '<td class="text-center">' + defaultCell + '</td>' +
                '<td class="text-end">' + actions + '</td>' +
                '</tr>'
            );
        });
    }

    function loadCompanies() {
        return $.getJSON('/api/company/manage')
            .done(renderCompaniesTable)
            .fail(function (xhr) {
                $('#companies-table-body').html(
                    '<tr><td colspan="6" class="text-danger text-center">' +
                    escapeHtml(getApiErrorMessage(xhr, 'Failed to load companies.')) +
                    '</td></tr>'
                );
            });
    }

    function refreshNavbarCompanies() {
        if (window.ErpApp && typeof window.ErpApp.loadCompanies === 'function') {
            window.ErpApp.loadCompanies();
        }
    }

    function openCreateCompanyModal() {
        clearCompanyFormError();
        $('#companyModalLabel').text('Add Company');
        $('#manage-company-id').val('');
        $('#company-form')[0].reset();
        $('#manage-company-default').prop('checked', false);
        companyModal.show();
    }

    function openEditCompanyModal(id) {
        clearCompanyFormError();
        $.getJSON('/api/company/' + id)
            .done(function (c) {
                $('#companyModalLabel').text('Edit Company');
                $('#manage-company-id').val(c.id);
                $('#manage-company-name').val(c.companyName);
                $('#manage-company-ntn').val(c.ntn || '');
                $('#manage-company-address').val(c.address || '');
                $('#manage-company-phone').val(c.phone || '');
                $('#manage-company-email').val(c.email || '');
                $('#manage-province-id').val(c.provinceId || '');
                $('#manage-company-default').prop('checked', c.isDefault === true);
                companyModal.show();
            })
            .fail(function (xhr) {
                showError(getApiErrorMessage(xhr, 'Failed to load company.'));
            });
    }

    function saveCompany(e) {
        e.preventDefault();
        clearCompanyFormError();

        var id = parseInt($('#manage-company-id').val(), 10) || null;
        var provinceVal = $('#manage-province-id').val();
        var payload = {
            id: id,
            companyName: $('#manage-company-name').val().trim(),
            address: $('#manage-company-address').val().trim() || null,
            ntn: $('#manage-company-ntn').val().trim() || null,
            provinceId: provinceVal ? parseInt(provinceVal, 10) : null,
            phone: $('#manage-company-phone').val().trim() || null,
            email: $('#manage-company-email').val().trim() || null,
            isDefault: $('#manage-company-default').is(':checked')
        };

        var $btn = $('#btn-save-company');
        $btn.prop('disabled', true);

        $.ajax({
            url: id ? '/api/company/' + id : '/api/company',
            method: id ? 'PUT' : 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
            .done(function (result) {
                companyModal.hide();
                showSuccess(result.message || 'Company saved.');
                loadCompanies();
                refreshNavbarCompanies();
            })
            .fail(function (xhr) {
                showCompanyFormError(getApiErrorMessage(xhr, 'Could not save company.'));
            })
            .always(function () {
                $btn.prop('disabled', false);
            });
    }

    function deleteCompany(id) {
        if (!window.confirm('Delete this company? This cannot be undone.')) {
            return;
        }

        $.ajax({
            url: '/api/company/' + id,
            method: 'DELETE'
        })
            .done(function (result) {
                showSuccess(result.message || 'Company deleted.');
                loadCompanies();
                refreshNavbarCompanies();
                $.getJSON('/api/company/current').fail(function () {
                    $('#settings-company-warning')
                        .removeClass('d-none')
                        .text('Select a company from the top navbar to manage settings.');
                });
            })
            .fail(function (xhr) {
                showError(getApiErrorMessage(xhr, 'Could not delete company.'));
            });
    }

    function setDefaultCompany(id) {
        $.ajax({
            url: '/api/company/' + id + '/set-default',
            method: 'POST'
        })
            .done(function (result) {
                showSuccess(result.message || 'Default company updated.');
                loadCompanies();
                refreshNavbarCompanies();
            })
            .fail(function (xhr) {
                showError(getApiErrorMessage(xhr, 'Could not set default company.'));
            });
    }

    function saveSettings(e) {
        e.preventDefault();
        if (!canEdit) return;

        clearMessages();

        var provinceVal = $('#province-id').val();
        var payload = {
            companyName: $('#company-name').val().trim(),
            address: $('#company-address').val().trim() || null,
            ntn: $('#company-ntn').val().trim() || null,
            provinceId: provinceVal ? parseInt(provinceVal, 10) : null,
            phone: $('#company-phone').val().trim() || null,
            email: $('#company-email').val().trim() || null,
            fbrPostUrl: $('#fbr-post-url').val().trim() || null,
            apiToken: $('#api-token').val().trim() || null,
            clearApiToken: $('#clear-api-token').is(':checked'),
            salesTaxRate: parseFloat($('#sales-tax-rate').val()) || 0,
            unregisteredSalesTaxRate: parseFloat($('#unreg-tax-rate').val()) || 0
        };

        var $btn = $('#btn-save-settings');
        $btn.prop('disabled', true);

        $.ajax({
            url: '/api/company-settings',
            method: 'PUT',
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
            .done(function (result) {
                showSuccess(result.message || 'Settings saved.');
                if (result.settings) {
                    populateForm(result.settings);
                } else {
                    loadSettings();
                }
                loadCompanies();
                refreshNavbarCompanies();
            })
            .fail(function (xhr) {
                showError(getApiErrorMessage(xhr, 'Could not save settings.'));
            })
            .always(function () {
                $btn.prop('disabled', false);
            });
    }

    function applyReadOnly() {
        if (canEdit) return;
        $('#company-settings-form input, #company-settings-form select, #company-settings-form textarea')
            .prop('readonly', true)
            .prop('disabled', true);
        $('#btn-save-settings').remove();
    }

    function applyCompanyPermissions() {
        if (!canCreate) {
            $('#btn-add-company').remove();
        }
    }

    $(function () {
        canCreate = $('#settings-permissions').data('can-create') === true;
        canEdit = $('#settings-permissions').data('can-edit') === true;
        canDelete = $('#settings-permissions').data('can-delete') === true;

        companyModal = new bootstrap.Modal(document.getElementById('companyModal'));

        if (window.location.hash === '#tax') {
            setTimeout(function () {
                document.getElementById('tax-section').scrollIntoView({ behavior: 'smooth' });
            }, 500);
        }

        loadProvinces().always(function () {
            loadCompanies();
        });

        $.getJSON('/api/company/current')
            .done(function () {
                $('#settings-company-warning').addClass('d-none');
                loadSettings().fail(function (xhr) {
                    showError(getApiErrorMessage(xhr, 'Failed to load settings.'));
                });
            })
            .fail(function () {
                $('#settings-company-warning')
                    .removeClass('d-none')
                    .text('Select a company from the top navbar to manage profile and FBR settings below.');
            });

        $('#company-settings-form').on('submit', saveSettings);
        $('#company-form').on('submit', saveCompany);
        $('#btn-add-company').on('click', openCreateCompanyModal);
        $('#companies-table-body').on('click', '.btn-edit-company', function () {
            openEditCompanyModal(parseInt($(this).data('id'), 10));
        });
        $('#companies-table-body').on('click', '.btn-delete-company', function () {
            deleteCompany(parseInt($(this).data('id'), 10));
        });
        $('#companies-table-body').on('click', '.btn-set-default', function () {
            setDefaultCompany(parseInt($(this).data('id'), 10));
        });

        $('#clear-api-token').on('change', function () {
            if ($(this).is(':checked')) {
                $('#api-token').val('').prop('disabled', true);
            } else {
                $('#api-token').prop('disabled', false);
            }
        });

        applyReadOnly();
        applyCompanyPermissions();
    });
})();
