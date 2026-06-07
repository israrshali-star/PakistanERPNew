(function () {
    'use strict';

    var dataTable = null;
    var fiscalYearModal = null;
    var canEdit = false;

    function showError(message) {
        $('#fiscal-year-form-error').removeClass('d-none').text(message);
    }

    function clearError() {
        $('#fiscal-year-form-error').addClass('d-none').text('');
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        return body && (body.message || body.Message) ? (body.message || body.Message) : fallback;
    }

    function formatDate(dateValue) {
        if (!dateValue) return '—';
        var date = new Date(dateValue);
        return isNaN(date.getTime()) ? '—' : date.toLocaleDateString();
    }

    function toDateInputValue(dateValue) {
        if (!dateValue) return '';
        var date = new Date(dateValue);
        if (isNaN(date.getTime())) return '';
        return date.toISOString().slice(0, 10);
    }

    function loadDataTable() {
        if (dataTable) {
            dataTable.ajax.reload();
            return;
        }

        dataTable = $('#fiscal-years-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: {
                url: '/api/fiscal-years/datatable',
                error: function (xhr) {
                    if (xhr.status === 400) {
                        $('#fiscal-year-company-warning')
                            .removeClass('d-none')
                            .text(getApiErrorMessage(xhr, 'Select a company first.'));
                    }
                }
            },
            order: [[2, 'desc']],
            pageLength: 25,
            columns: [
                { data: 'code', render: function (d) { return '<code>' + (d || '') + '</code>'; } },
                { data: 'name' },
                { data: 'startDate', render: function (d) { return formatDate(d); } },
                { data: 'endDate', render: function (d) { return formatDate(d); } },
                {
                    data: 'isActive',
                    render: function (d) { return d ? '<span class="badge bg-success">Active</span>' : '—'; }
                },
                {
                    data: 'isClosed',
                    render: function (d) { return d ? '<span class="badge bg-dark">Closed</span>' : '<span class="badge bg-light text-dark">Open</span>'; }
                },
                {
                    data: 'id',
                    orderable: false,
                    className: 'text-end',
                    render: function (id, type, row) {
                        if (!canEdit) return '—';
                        var actions = '<button type="button" class="btn btn-link btn-sm p-0 me-1 btn-edit-fiscal-year" data-id="' + id + '"><i class="fa-solid fa-pen"></i></button>';
                        if (!row.isActive) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 me-1 btn-set-active" data-id="' + id + '"><i class="fa-solid fa-check"></i></button>';
                        }
                        actions += '<button type="button" class="btn btn-link btn-sm p-0 text-danger btn-delete-fiscal-year" data-id="' + id + '"><i class="fa-solid fa-trash"></i></button>';
                        return actions;
                    }
                }
            ]
        });
    }

    function openCreate() {
        clearError();
        $('#fiscalYearModalLabel').text('New Fiscal Year');
        $('#fiscal-year-id').val('');
        $('#fiscal-year-form')[0].reset();
        $('#fiscal-year-active').prop('checked', true);
        $('#fiscal-year-closed').prop('checked', false);
        fiscalYearModal.show();
    }

    function openEdit(id) {
        clearError();
        $.getJSON('/api/fiscal-years/' + id)
            .done(function (fy) {
                $('#fiscalYearModalLabel').text('Edit Fiscal Year');
                $('#fiscal-year-id').val(fy.id);
                $('#fiscal-year-code').val(fy.code || '');
                $('#fiscal-year-name').val(fy.name || '');
                $('#fiscal-year-start').val(toDateInputValue(fy.startDate));
                $('#fiscal-year-end').val(toDateInputValue(fy.endDate));
                $('#fiscal-year-active').prop('checked', fy.isActive === true);
                $('#fiscal-year-closed').prop('checked', fy.isClosed === true);
                fiscalYearModal.show();
            })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Failed to load fiscal year.'));
            });
    }

    function saveFiscalYear(e) {
        e.preventDefault();
        clearError();

        var id = $('#fiscal-year-id').val();
        var payload = {
            id: id ? parseInt(id, 10) : null,
            code: $('#fiscal-year-code').val().trim() || null,
            name: $('#fiscal-year-name').val().trim(),
            startDate: $('#fiscal-year-start').val(),
            endDate: $('#fiscal-year-end').val(),
            isActive: $('#fiscal-year-active').is(':checked'),
            isClosed: $('#fiscal-year-closed').is(':checked')
        };

        $.ajax({
            url: id ? '/api/fiscal-years/' + id : '/api/fiscal-years',
            method: id ? 'PUT' : 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
            .done(function () {
                fiscalYearModal.hide();
                dataTable.ajax.reload(null, false);
            })
            .fail(function (xhr) {
                showError(getApiErrorMessage(xhr, 'Could not save fiscal year.'));
            });
    }

    function setActive(id) {
        $.post('/api/fiscal-years/' + id + '/set-active')
            .done(function () {
                dataTable.ajax.reload(null, false);
            })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Could not set active fiscal year.'));
            });
    }

    function deleteFiscalYear(id) {
        if (!confirm('Delete this fiscal year?')) return;
        $.ajax({ url: '/api/fiscal-years/' + id, method: 'DELETE' })
            .done(function () {
                dataTable.ajax.reload(null, false);
            })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Could not delete fiscal year.'));
            });
    }

    $(function () {
        canEdit = $('#fiscal-year-permissions').data('can-edit') === true;
        if (!canEdit) {
            $('#btn-add-fiscal-year').remove();
        }

        fiscalYearModal = new bootstrap.Modal(document.getElementById('fiscalYearModal'));

        $.getJSON('/api/company/current')
            .done(function () {
                $('#fiscal-year-company-warning').addClass('d-none');
                loadDataTable();
            })
            .fail(function () {
                $('#fiscal-year-company-warning')
                    .removeClass('d-none')
                    .text('Select a company from the top navbar.');
            });

        $('#btn-add-fiscal-year').on('click', openCreate);
        $('#fiscal-year-form').on('submit', saveFiscalYear);
        $('#fiscal-years-table').on('click', '.btn-edit-fiscal-year', function () { openEdit($(this).data('id')); });
        $('#fiscal-years-table').on('click', '.btn-set-active', function () { setActive($(this).data('id')); });
        $('#fiscal-years-table').on('click', '.btn-delete-fiscal-year', function () { deleteFiscalYear($(this).data('id')); });
    });
})();
