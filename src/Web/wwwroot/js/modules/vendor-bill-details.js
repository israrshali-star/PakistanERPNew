(function () {
    'use strict';

    function showMessage(type, text) {
        $('#bill-action-message')
            .removeClass('d-none alert-success alert-danger alert-info')
            .addClass('alert-' + type)
            .text(text);
    }

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        if (!body) {
            return fallback;
        }
        return body.message || body.Message || fallback;
    }

    function postAction(url, method, successMessage, onSuccess) {
        var billId = $('#bill-detail').data('id');
        $.ajax({
            url: '/api/vendor-bills/' + billId + url,
            method: method || 'POST'
        })
            .done(function (result) {
                if (onSuccess) {
                    onSuccess(result);
                    return;
                }
                showMessage('success', result.message || successMessage);
                setTimeout(function () {
                    window.location.reload();
                }, 800);
            })
            .fail(function (xhr) {
                showMessage('danger', getApiErrorMessage(xhr, 'Action failed.'));
            });
    }

    $(function () {
        var $detail = $('#bill-detail');

        if ($detail.data('can-approve') === true) {
            $('#btn-approve-bill').on('click', function () {
                if (!confirm('Approve this bill and post to the general ledger?')) {
                    return;
                }
                postAction('/approve', 'POST', 'Bill approved and posted to GL.');
            });
        }

        if ($detail.data('can-revert-to-draft') === true) {
            $('#btn-revert-bill').on('click', function () {
                if (!confirm('Reopen this bill as a draft? Unsold lines can be edited. Lines with sold inventory will stay locked.')) {
                    return;
                }
                postAction('/revert-to-draft', 'POST', 'Bill reopened as draft.', function () {
                    window.location.href = '/VendorBills/Edit/' + $detail.data('id');
                });
            });
        }

        if ($detail.data('can-delete') === true) {
            $('#btn-delete-bill').on('click', function () {
                if (!confirm('Delete this draft bill? It cannot be undone.')) {
                    return;
                }
                postAction('', 'DELETE', 'Bill deleted.', function () {
                    window.location.href = '/VendorBills';
                });
            });
        }

        $('#bill-attachment-upload').on('change', function () {
            var billId = $detail.data('id');
            var input = this;
            if (!input.files || input.files.length === 0) {
                return;
            }

            var uploads = Array.prototype.map.call(input.files, function (file) {
                var formData = new FormData();
                formData.append('file', file);
                return $.ajax({
                    url: '/api/vendor-bills/' + billId + '/attachments',
                    method: 'POST',
                    data: formData,
                    processData: false,
                    contentType: false
                });
            });

            $.when.apply($, uploads)
                .done(function () {
                    window.location.reload();
                })
                .fail(function (xhr) {
                    showMessage('danger', getApiErrorMessage(xhr, 'Failed to upload attachment.'));
                })
                .always(function () {
                    input.value = '';
                });
        });

        $('#bill-attachments-card').on('click', '.btn-delete-attachment', function () {
            var attachmentId = $(this).data('id');
            if (!confirm('Delete this attachment?')) {
                return;
            }

            $.ajax({
                url: '/api/vendor-bills/attachments/' + attachmentId,
                method: 'DELETE'
            })
                .done(function () {
                    window.location.reload();
                })
                .fail(function (xhr) {
                    showMessage('danger', getApiErrorMessage(xhr, 'Failed to delete attachment.'));
                });
        });
    });
})();

