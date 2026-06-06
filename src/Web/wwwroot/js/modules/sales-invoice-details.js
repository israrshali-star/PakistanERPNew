(function () {
    'use strict';

    function showMessage(type, text) {
        $('#invoice-action-message')
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

    function postAction(url, successMessage) {
        var invoiceId = $('#invoice-detail').data('id');
        $.ajax({
            url: '/api/sales-invoices/' + invoiceId + url,
            method: 'POST'
        })
            .done(function (result) {
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
        var $detail = $('#invoice-detail');

        if ($detail.data('can-post') === true) {
            $('#btn-post-invoice').on('click', function () {
                if (!confirm('Post this invoice to the general ledger?')) {
                    return;
                }
                postAction('/post', 'Invoice posted to GL.');
            });
        }

        if ($detail.data('can-submit-fbr') === true) {
            $('#btn-submit-fbr').on('click', function () {
                if (!confirm('Submit this invoice to FBR?')) {
                    return;
                }
                postAction('/submit-fbr', 'Invoice submitted to FBR.');
            });
        }

        if ($detail.data('can-cancel') === true) {
            $('#btn-cancel-invoice').on('click', function () {
                if (!confirm('Cancel this draft invoice?')) {
                    return;
                }
                postAction('/cancel', 'Invoice cancelled.');
            });
        }
    });
})();
