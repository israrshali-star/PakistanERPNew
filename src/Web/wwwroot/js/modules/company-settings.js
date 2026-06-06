(function () {
    'use strict';

    var canEdit = false;
    var hasExistingToken = false;

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

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        if (!body) return fallback;
        return body.message || body.Message || fallback;
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

    function loadProvinces() {
        return $.getJSON('/api/lookup/provinces').done(function (provinces) {
            var $select = $('#province-id');
            (provinces || []).forEach(function (p) {
                $select.append($('<option></option>').val(p.id).text(p.name));
            });
        });
    }

    function loadSettings() {
        return $.getJSON('/api/company-settings').done(populateForm);
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

    $(function () {
        canEdit = $('#settings-permissions').data('can-edit') === true;

        if (window.location.hash === '#tax') {
            setTimeout(function () {
                document.getElementById('tax-section').scrollIntoView({ behavior: 'smooth' });
            }, 500);
        }

        $.getJSON('/api/company/current')
            .done(function () {
                $('#settings-company-warning').addClass('d-none');
                loadProvinces().always(function () {
                    loadSettings().fail(function (xhr) {
                        showError(getApiErrorMessage(xhr, 'Failed to load settings.'));
                    });
                });
            })
            .fail(function () {
                $('#settings-company-warning')
                    .removeClass('d-none')
                    .text('Select a company from the top navbar to manage settings.');
            });

        $('#company-settings-form').on('submit', saveSettings);
        $('#clear-api-token').on('change', function () {
            if ($(this).is(':checked')) {
                $('#api-token').val('').prop('disabled', true);
            } else {
                $('#api-token').prop('disabled', false);
            }
        });

        applyReadOnly();
    });
})();
