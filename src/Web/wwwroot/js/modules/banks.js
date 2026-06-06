(function () {
    'use strict';

    var bankModal = null;
    var dataTable = null;
    var canCreate = false;
    var canEdit = false;
    var canDelete = false;

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function formatMoney(value) {
        return (parseFloat(value) || 0).toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function showFormError(message) {
        $('#bank-form-error').removeClass('d-none').text(message);
    }

    function clearFormError() {
        $('#bank-form-error').addClass('d-none').text('');
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        return body && (body.message || body.Message) ? (body.message || body.Message) : fallback;
    }

    function loadChartOfAccounts() {
        return $.getJSON('/api/banks/chart-of-accounts').done(function (accounts) {
            var $select = $('#chart-of-account-id');
            $select.find('option:not(:first)').remove();
            (accounts || []).forEach(function (a) {
                $select.append($('<option></option>').val(a.id).text(a.accountNumber + ' — ' + a.accountName));
            });
        });
    }

    function initDataTable() {
        if (dataTable) {
            dataTable.ajax.reload();
            return;
        }

        dataTable = $('#banks-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: {
                url: '/api/banks/datatable',
                error: function (xhr) {
                    if (xhr.status === 400) {
                        $('#bank-company-warning').removeClass('d-none').text(getApiErrorMessage(xhr, 'Select a company first.'));
                    }
                }
            },
            order: [[0, 'asc']],
            pageLength: 25,
            columns: [
                { data: 'bankName' },
                { data: 'accountTitle' },
                { data: 'accountNumber', render: function (d) { return '<code>' + escapeHtml(d) + '</code>'; } },
                { data: 'currentBalance', className: 'text-end text-currency', render: formatMoney },
                {
                    data: 'isActive',
                    render: function (d) {
                        return d ? '<span class="badge bg-success">Active</span>' : '<span class="badge bg-secondary">Inactive</span>';
                    }
                },
                {
                    data: 'id',
                    orderable: false,
                    className: 'text-end',
                    render: function (id) {
                        var actions = '';
                        if (canEdit) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 me-1 btn-edit-bank" data-id="' + id + '"><i class="fa-solid fa-pen"></i></button>';
                        }
                        if (canDelete) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 text-danger btn-delete-bank" data-id="' + id + '"><i class="fa-solid fa-trash"></i></button>';
                        }
                        return actions || '—';
                    }
                }
            ],
            language: { emptyTable: 'No bank accounts found.' }
        });
    }

    function openCreateModal() {
        clearFormError();
        $('#bankModalLabel').text('New Bank Account');
        $('#bank-id').val('');
        $('#bank-form')[0].reset();
        $('#bank-active').prop('checked', true);
        $('#opening-balance').prop('readonly', false);
        bankModal.show();
    }

    function openEditModal(id) {
        clearFormError();
        $.getJSON('/api/banks/' + id)
            .done(function (b) {
                $('#bankModalLabel').text('Edit Bank Account');
                $('#bank-id').val(b.id);
                $('#bank-name').val(b.bankName);
                $('#account-title').val(b.accountTitle);
                $('#account-number').val(b.accountNumber);
                $('#iban').val(b.iban || '');
                $('#opening-balance').val(b.openingBalance);
                $('#opening-balance').prop('readonly', b.transactionCount > 0);
                $('#chart-of-account-id').val(b.chartOfAccountId || '').trigger('change');
                $('#bank-active').prop('checked', b.isActive);
                bankModal.show();
            })
            .fail(function () { alert('Failed to load bank account.'); });
    }

    function saveBank(e) {
        e.preventDefault();
        clearFormError();

        var coaId = parseInt($('#chart-of-account-id').val(), 10) || 0;
        var payload = {
            id: parseInt($('#bank-id').val(), 10) || null,
            bankName: $('#bank-name').val().trim(),
            accountTitle: $('#account-title').val().trim(),
            accountNumber: $('#account-number').val().trim(),
            iban: $('#iban').val().trim() || null,
            chartOfAccountId: coaId > 0 ? coaId : null,
            openingBalance: parseFloat($('#opening-balance').val()) || 0,
            isActive: $('#bank-active').is(':checked')
        };

        var id = payload.id;
        $.ajax({
            url: id ? '/api/banks/' + id : '/api/banks',
            method: id ? 'PUT' : 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
            .done(function () {
                bankModal.hide();
                dataTable.ajax.reload(null, false);
            })
            .fail(function (xhr) {
                showFormError(getApiErrorMessage(xhr, 'Could not save bank account.'));
            });
    }

    function deleteBank(id) {
        if (!confirm('Delete this bank account?')) return;
        $.ajax({ url: '/api/banks/' + id, method: 'DELETE' })
            .done(function () { dataTable.ajax.reload(null, false); })
            .fail(function (xhr) { alert(getApiErrorMessage(xhr, 'Could not delete bank account.')); });
    }

    $(function () {
        var $perms = $('#bank-permissions');
        canCreate = $perms.data('can-create') === true;
        canEdit = $perms.data('can-edit') === true;
        canDelete = $perms.data('can-delete') === true;
        if (!canCreate) $('#btn-add-bank').remove();

        bankModal = new bootstrap.Modal(document.getElementById('bankModal'));
        $('#chart-of-account-id').select2({ theme: 'bootstrap-5', width: '100%', dropdownParent: $('#bankModal') });

        $.getJSON('/api/company/current')
            .done(function () {
                $('#bank-company-warning').addClass('d-none');
                loadChartOfAccounts().always(initDataTable);
            })
            .fail(function () {
                $('#bank-company-warning').removeClass('d-none').text('Select a company from the top navbar.');
            });

        $('#btn-add-bank').on('click', openCreateModal);
        $('#bank-form').on('submit', saveBank);
        $('#banks-table').on('click', '.btn-edit-bank', function () { openEditModal($(this).data('id')); });
        $('#banks-table').on('click', '.btn-delete-bank', function () { deleteBank($(this).data('id')); });
    });
})();
