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
    });
})();
