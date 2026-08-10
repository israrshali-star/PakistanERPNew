(function (window) {
    'use strict';

    var fbrModal = null;

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        if (!body) {
            return fallback;
        }
        return body.message || body.Message || fallback;
    }

    function getResponseJson(body) {
        if (!body) {
            return null;
        }
        return body.responseJson || body.ResponseJson || null;
    }

    function formatJsonText(value) {
        if (!value) {
            return '';
        }
        if (typeof value !== 'string') {
            try {
                return JSON.stringify(value, null, 2);
            } catch (e) {
                return String(value);
            }
        }
        try {
            return JSON.stringify(JSON.parse(value), null, 2);
        } catch (e) {
            return value;
        }
    }

    function showFbrErrorInModal(xhr) {
        var body = xhr && xhr.responseJSON;
        var message = getApiErrorMessage(xhr, 'FBR submission failed.');
        var responseJson = getResponseJson(body);

        if (responseJson) {
            $('#fbr-payload-json').text(formatJsonText(responseJson));
            $('#fbr-payload-hint').text('FBR API response (submission failed). Fix the issues below and submit again.');
        }

        $('#fbr-payload-error').removeClass('d-none').text(message);
        $('#btn-confirm-fbr-submit').prop('disabled', false);
    }

    function ensureModal() {
        if (fbrModal) {
            return fbrModal;
        }

        if (!document.getElementById('fbrPayloadModal')) {
            return null;
        }

        fbrModal = new bootstrap.Modal(document.getElementById('fbrPayloadModal'));
        return fbrModal;
    }

    function submitToFbr(invoiceId, onSuccess, onError) {
        $.ajax({
            url: '/api/sales-invoices/' + invoiceId + '/submit-fbr',
            method: 'POST'
        })
            .done(function (result) {
                if (onSuccess) {
                    onSuccess(result);
                }
            })
            .fail(function (xhr) {
                if (onError) {
                    onError(xhr);
                    return;
                }
                alert(getApiErrorMessage(xhr, 'FBR submission failed.'));
            });
    }

    function showFbrPreviewAndSubmit(invoiceId, onSuccess) {
        var modal = ensureModal();
        if (!modal) {
            if (!window.confirm('Submit this invoice to FBR?')) {
                return;
            }
            submitToFbr(invoiceId, onSuccess);
            return;
        }

        $('#fbr-payload-json').text('Loading FBR JSON payload...');
        $('#fbr-payload-hint').text('');
        $('#fbr-payload-error').addClass('d-none').text('');
        $('#btn-confirm-fbr-submit').prop('disabled', true).data('invoice-id', invoiceId);

        $.getJSON('/api/sales-invoices/' + invoiceId + '/fbr-payload')
            .done(function (preview) {
                $('#fbr-payload-json').text(preview.payloadJson || preview.PayloadJson || '{}');
                var simulation = preview.isSimulationMode === true || preview.IsSimulationMode === true;
                $('#fbr-payload-hint').text(
                    simulation
                        ? 'Simulation mode: company FBR URL or API token is not configured.'
                        : 'Live mode: this JSON will be posted to the configured FBR API.'
                );
                $('#btn-confirm-fbr-submit').prop('disabled', false);
                modal.show();
            })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Could not load FBR payload preview.'));
            });

        $('#btn-confirm-fbr-submit').off('click.fbr').on('click.fbr', function () {
            var id = $(this).data('invoice-id');
            var $btn = $(this);
            $btn.prop('disabled', true);
            $('#fbr-payload-error').addClass('d-none').text('');
            submitToFbr(
                id,
                function (result) {
                    modal.hide();
                    if (onSuccess) {
                        onSuccess(result);
                    }
                },
                function (xhr) {
                    showFbrErrorInModal(xhr);
                }
            );
        });
    }

    function downloadInvoicePdf(invoiceId) {
        window.open('/api/sales-invoices/' + invoiceId + '/pdf', '_blank');
    }

    window.SalesInvoiceFbr = {
        showFbrPreviewAndSubmit: showFbrPreviewAndSubmit,
        downloadInvoicePdf: downloadInvoicePdf
    };
})(window);
