(function () {
    'use strict';

    function escapeHtml(text) {
        return $('<div>').text(text ?? '').html();
    }

    function initRolesListPage() {
        var $table = $('#roles-table');
        if ($table.length === 0) return;

        $.getJSON('/api/roles')
            .done(function (roles) {
                var rows = roles.map(function (role) {
                    return '<tr>' +
                        '<td>' + escapeHtml(role.name) + '</td>' +
                        '<td class="text-end"><a class="btn btn-link btn-sm p-0" href="/Roles/Permissions/' + encodeURIComponent(role.id) + '"><i class="fa-solid fa-shield-halved me-1"></i>Permissions</a></td>' +
                        '</tr>';
                }).join('');
                $('#roles-table tbody').html(rows || '<tr><td colspan="2" class="text-muted text-center">No roles found.</td></tr>');
            })
            .fail(function () {
                $('#roles-table tbody').html('<tr><td colspan="2" class="text-danger text-center">Failed to load roles.</td></tr>');
            });
    }

    function initRolePermissionsPage() {
        var $page = $('#role-permissions-page');
        if ($page.length === 0) return;

        var roleId = $page.data('role-id');
        $('#btn-save-role-permissions').on('click', function () {
            var payload = {
                permissions: $('.permission-toggle').map(function () {
                    return {
                        permissionId: parseInt($(this).data('permission-id'), 10),
                        allowed: $(this).is(':checked')
                    };
                }).get()
            };

            $.ajax({
                url: '/api/roles/' + roleId + '/permissions',
                method: 'PUT',
                contentType: 'application/json',
                data: JSON.stringify(payload)
            })
                .done(function (result) {
                    $('#role-permissions-message')
                        .removeClass('d-none alert-danger')
                        .addClass('alert-success')
                        .text(result.message || 'Permissions updated.');
                })
                .fail(function (xhr) {
                    var message = (xhr.responseJSON && (xhr.responseJSON.message || xhr.responseJSON.Message)) || 'Could not update permissions.';
                    $('#role-permissions-message')
                        .removeClass('d-none alert-success')
                        .addClass('alert-danger')
                        .text(message);
                });
        });
    }

    $(function () {
        initRolesListPage();
        initRolePermissionsPage();
    });
})();
