(function ($) {
    'use strict';

    function clearMessages() {
        $('#change-password-error, #change-password-success').addClass('d-none').text('');
    }

    function showError(message) {
        $('#change-password-success').addClass('d-none').text('');
        $('#change-password-error').removeClass('d-none').text(message);
    }

    function showSuccess(message) {
        $('#change-password-error').addClass('d-none').text('');
        $('#change-password-success').removeClass('d-none').text(message);
    }

    function resetForm() {
        var form = document.getElementById('change-password-form');
        if (form) {
            form.reset();
        }
        clearMessages();
    }

    $(function () {
        var $modal = $('#changePasswordModal');
        if (!$modal.length) {
            return;
        }

        $modal.on('hidden.bs.modal', resetForm);

        $('#change-password-form').on('submit', function (e) {
            e.preventDefault();
            clearMessages();

            var newPassword = $('#new-password').val() || '';
            var confirmPassword = $('#confirm-password').val() || '';

            if (newPassword !== confirmPassword) {
                showError('New password and confirmation do not match.');
                return;
            }

            var $btn = $('#btn-save-password');
            $btn.prop('disabled', true);

            $.ajax({
                url: '/Account/ChangePassword',
                method: 'POST',
                data: $(this).serialize()
            })
                .done(function (result) {
                    showSuccess(result.message || 'Password changed successfully.');
                    $('#current-password, #new-password, #confirm-password').val('');
                    window.setTimeout(function () {
                        bootstrap.Modal.getOrCreateInstance($modal[0]).hide();
                    }, 1200);
                })
                .fail(function (xhr) {
                    var body = xhr.responseJSON;
                    showError((body && (body.message || body.Message)) || 'Could not change password.');
                })
                .always(function () {
                    $btn.prop('disabled', false);
                });
        });
    });
})(jQuery);
