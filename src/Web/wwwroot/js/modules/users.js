(function () {
    'use strict';

    var userModal = null;
    var dataTable = null;
    var canCreate = false;
    var canEdit = false;
    var canDelete = false;
    var roleLookups = [];
    var companyLookups = [];

    function showFormError(message) {
        $('#user-form-error').removeClass('d-none').text(message);
    }

    function clearFormError() {
        $('#user-form-error').addClass('d-none').text('');
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        return body && (body.message || body.Message) ? (body.message || body.Message) : fallback;
    }

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function populateLookups() {
        var $roles = $('#user-roles');
        var $companies = $('#user-companies');
        $roles.empty();
        $companies.empty();

        roleLookups.forEach(function (name) {
            $roles.append($('<option></option>').val(name).text(name));
        });

        companyLookups.forEach(function (company) {
            $companies.append($('<option></option>').val(company.id).text(company.name));
        });
    }

    function initDataTable() {
        if (dataTable) {
            dataTable.ajax.reload();
            return;
        }

        dataTable = $('#users-table').DataTable({
            processing: true,
            serverSide: true,
            ajax: '/api/users/datatable',
            order: [[0, 'asc']],
            pageLength: 25,
            columns: [
                { data: 'email', render: function (d) { return escapeHtml(d); } },
                { data: 'fullName', render: function (d) { return escapeHtml(d); } },
                {
                    data: 'isActive',
                    render: function (d) {
                        return d ? '<span class="badge bg-success">Active</span>' : '<span class="badge bg-secondary">Inactive</span>';
                    }
                },
                {
                    data: 'roles',
                    render: function (roles) {
                        if (!roles || roles.length === 0) return '—';
                        return roles.map(function (r) { return '<span class="badge bg-light text-dark me-1">' + escapeHtml(r) + '</span>'; }).join('');
                    }
                },
                {
                    data: 'companies',
                    render: function (companies) {
                        if (!companies || companies.length === 0) return '—';
                        return companies.map(function (c) { return '<span class="badge bg-info-subtle text-dark me-1">' + escapeHtml(c.name) + '</span>'; }).join('');
                    }
                },
                {
                    data: 'id',
                    orderable: false,
                    className: 'text-end',
                    render: function (id) {
                        var actions = '';
                        if (canEdit) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 me-1 btn-edit-user" data-id="' + id + '" title="Edit"><i class="fa-solid fa-pen"></i></button>';
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 me-1 btn-reset-password" data-id="' + id + '" title="Reset Password"><i class="fa-solid fa-key"></i></button>';
                        }
                        if (canDelete) {
                            actions += '<button type="button" class="btn btn-link btn-sm p-0 text-danger btn-delete-user" data-id="' + id + '" title="Delete"><i class="fa-solid fa-trash"></i></button>';
                        }
                        return actions || '—';
                    }
                }
            ]
        });
    }

    function openCreateModal() {
        clearFormError();
        $('#userModalLabel').text('New User');
        $('#user-id').val('');
        $('#user-form')[0].reset();
        $('#user-email').prop('disabled', false);
        $('#password-wrapper').removeClass('d-none');
        $('#user-password').prop('required', true).val('');
        $('#user-active').prop('checked', true);
        $('#user-roles').val([]);
        $('#user-companies').val([]);
        userModal.show();
    }

    function openEditModal(id) {
        clearFormError();
        $.getJSON('/api/users/' + id)
            .done(function (u) {
                $('#userModalLabel').text('Edit User');
                $('#user-id').val(u.id);
                $('#user-email').val(u.email).prop('disabled', true);
                $('#user-full-name').val(u.fullName || '');
                $('#password-wrapper').addClass('d-none');
                $('#user-password').prop('required', false).val('');
                $('#user-active').prop('checked', u.isActive === true);
                $('#user-roles').val((u.roles || []).map(function (x) { return x; }));
                $('#user-companies').val((u.companies || []).map(function (x) { return String(x.id); }));
                userModal.show();
            })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Failed to load user.'));
            });
    }

    function collectPayload() {
        var roleNames = $('#user-roles').val() || [];
        var companyIds = ($('#user-companies').val() || []).map(function (id) { return parseInt(id, 10); });
        return {
            fullName: $('#user-full-name').val().trim(),
            isActive: $('#user-active').is(':checked'),
            roleNames: roleNames,
            companyIds: companyIds
        };
    }

    function saveUser(e) {
        e.preventDefault();
        clearFormError();

        var id = $('#user-id').val();
        var payload;
        var url;
        var method;

        if (id) {
            payload = collectPayload();
            url = '/api/users/' + id;
            method = 'PUT';
        } else {
            payload = collectPayload();
            payload.email = $('#user-email').val().trim();
            payload.password = $('#user-password').val();
            url = '/api/users';
            method = 'POST';
        }

        $.ajax({
            url: url,
            method: method,
            contentType: 'application/json',
            data: JSON.stringify(payload)
        })
            .done(function () {
                userModal.hide();
                dataTable.ajax.reload(null, false);
            })
            .fail(function (xhr) {
                showFormError(getApiErrorMessage(xhr, 'Could not save user.'));
            });
    }

    function resetPassword(id) {
        var password = prompt('Enter new password (min 8 chars):');
        if (!password) return;
        $.ajax({
            url: '/api/users/' + id + '/reset-password',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ newPassword: password })
        })
            .done(function () {
                alert('Password reset successfully.');
            })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Could not reset password.'));
            });
    }

    function deleteUser(id) {
        if (!confirm('Delete this user?')) return;
        $.ajax({ url: '/api/users/' + id, method: 'DELETE' })
            .done(function () {
                dataTable.ajax.reload(null, false);
            })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Could not delete user.'));
            });
    }

    function loadLookups() {
        return $.when(
            $.getJSON('/api/users/lookups/roles').done(function (roles) { roleLookups = roles || []; }),
            $.getJSON('/api/users/lookups/companies').done(function (companies) { companyLookups = companies || []; })
        ).then(function () {
            populateLookups();
        });
    }

    $(function () {
        var $perms = $('#users-permissions');
        canCreate = $perms.data('can-create') === true;
        canEdit = $perms.data('can-edit') === true;
        canDelete = $perms.data('can-delete') === true;

        if (!canCreate) {
            $('#btn-add-user').remove();
        }

        userModal = new bootstrap.Modal(document.getElementById('userModal'));

        loadLookups().always(function () {
            initDataTable();
        });

        $('#btn-add-user').on('click', openCreateModal);
        $('#user-form').on('submit', saveUser);
        $('#users-table').on('click', '.btn-edit-user', function () { openEditModal($(this).data('id')); });
        $('#users-table').on('click', '.btn-reset-password', function () { resetPassword($(this).data('id')); });
        $('#users-table').on('click', '.btn-delete-user', function () { deleteUser($(this).data('id')); });
    });
})();
