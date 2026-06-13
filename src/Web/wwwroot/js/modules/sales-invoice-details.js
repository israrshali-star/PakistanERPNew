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

    function downloadPdf() {
        var invoiceId = $('#invoice-detail').data('id');
        if (window.SalesInvoiceFbr) {
            window.SalesInvoiceFbr.downloadInvoicePdf(invoiceId);
        }
    }

    function downloadDeliveryChallan() {
        var invoiceId = $('#invoice-detail').data('id');
        window.open('/api/sales-invoices/' + invoiceId + '/delivery-challan-pdf', '_blank');
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
                var invoiceId = $detail.data('id');
                if (!window.SalesInvoiceFbr) {
                    if (!confirm('Submit this invoice to FBR?')) {
                        return;
                    }
                    postAction('/submit-fbr', 'Invoice submitted to FBR.');
                    return;
                }

                window.SalesInvoiceFbr.showFbrPreviewAndSubmit(invoiceId, function (result) {
                    showMessage('success', result.message || 'Invoice submitted to FBR.');
                    setTimeout(function () {
                        window.location.reload();
                    }, 800);
                });
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

        if ($detail.data('can-delete') === true) {
            $('#btn-delete-invoice').on('click', function () {
                var invoiceId = $detail.data('id');
                if (!confirm('Permanently delete this invoice and its GL journal entry? This cannot be undone.')) {
                    return;
                }

                $.ajax({
                    url: '/api/sales-invoices/' + invoiceId,
                    method: 'DELETE'
                })
                    .done(function () {
                        window.location.href = '/SalesInvoices';
                    })
                    .fail(function (xhr) {
                        showMessage('danger', getApiErrorMessage(xhr, 'Failed to delete invoice.'));
                    });
            });
        }

        $('#btn-download-challan').on('click', downloadDeliveryChallan);

        if ($detail.data('can-download-pdf') === true) {
            $('#btn-download-pdf, .btn-download-pdf-inline').on('click', downloadPdf);
        }

        $('#invoice-attachment-upload').on('change', function () {
            var invoiceId = $detail.data('id');
            var input = this;
            if (!input.files || input.files.length === 0) {
                return;
            }

            var uploads = Array.prototype.map.call(input.files, function (file) {
                var formData = new FormData();
                formData.append('file', file);
                return $.ajax({
                    url: '/api/sales-invoices/' + invoiceId + '/attachments',
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

        $('#invoice-attachments-card').on('click', '.btn-delete-attachment', function () {
            var attachmentId = $(this).data('id');
            if (!confirm('Delete this attachment?')) {
                return;
            }

            $.ajax({
                url: '/api/sales-invoices/attachments/' + attachmentId,
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
