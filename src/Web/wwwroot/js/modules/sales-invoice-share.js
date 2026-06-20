(function (window) {
    'use strict';

    var shareModal = null;
    var currentShareInfo = null;

    function getApiErrorMessage(xhr, fallback) {
        var body = xhr && xhr.responseJSON;
        if (!body) {
            return fallback;
        }
        return body.message || body.Message || fallback;
    }

    function ensureModal() {
        if (shareModal) {
            return shareModal;
        }

        if (!document.getElementById('invoiceShareModal')) {
            return null;
        }

        shareModal = new bootstrap.Modal(document.getElementById('invoiceShareModal'));
        return shareModal;
    }

    function formatCurrency(value) {
        var num = parseFloat(value) || 0;
        return 'PKR ' + num.toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function formatDate(value) {
        var dt = new Date(value);
        if (Number.isNaN(dt.getTime())) {
            return value;
        }
        return String(dt.getDate()).padStart(2, '0') + '/' +
            String(dt.getMonth() + 1).padStart(2, '0') + '/' +
            dt.getFullYear();
    }

    function normalizeWhatsAppPhone(phone) {
        var digits = String(phone || '').replace(/\D/g, '');
        if (!digits) {
            return '';
        }
        if (digits.startsWith('0')) {
            digits = '92' + digits.substring(1);
        }
        if (digits.length === 10 && digits.startsWith('3')) {
            digits = '92' + digits;
        }
        return digits;
    }

    function showShareAlert(type, message) {
        $('#share-error, #share-success').addClass('d-none');
        if (type === 'success') {
            $('#share-success').removeClass('d-none').text(message);
        } else if (message) {
            $('#share-error').removeClass('d-none').text(message);
        }
    }

    function populateModal(info) {
        currentShareInfo = info;
        $('#share-invoice-id').val(info.invoiceId);
        $('#share-customer-email').val(info.customerEmail || '');
        $('#share-customer-whatsapp').val(info.customerMobile || info.customerPhone || '');
        $('#share-customer-message').val('');
        $('#share-invoice-summary').text(
            info.customerName + ' · ' + info.invoiceNumber +
            ' · ' + formatDate(info.invoiceDate) + ' · ' + formatCurrency(info.netTotal)
        );

        if (!info.emailConfigured) {
            $('#share-email-hint').text('SMTP is not configured in appsettings.json. Email sending is disabled until SMTP is set up.');
            $('#btn-share-email').prop('disabled', true);
        } else {
            $('#share-email-hint').text('Sends invoice PDF as email attachment.');
            $('#btn-share-email').prop('disabled', false);
        }

        $('#share-godown-email').val(info.godownEmail || '');
        if (!info.canEmailChallan) {
            $('#share-challan-panel, #btn-share-email-challan, #btn-share-download-challan').addClass('d-none');
        } else {
            $('#share-challan-panel, #btn-share-email-challan, #btn-share-download-challan').removeClass('d-none');

            if (!info.emailConfigured) {
                $('#share-challan-hint').text('Configure SMTP in appsettings.json to email delivery challan.');
                $('#btn-share-email-challan').prop('disabled', true);
            } else if (!info.godownEmail) {
                $('#share-challan-hint').text('Set godown email in Company Settings or enter one below.');
                $('#btn-share-email-challan').prop('disabled', false);
            } else {
                $('#share-challan-hint').text('Emails delivery challan PDF to the godown address (company 3).');
                $('#btn-share-email-challan').prop('disabled', false);
            }
        }

        showShareAlert(null, null);
    }

    function downloadInvoicePdf(invoiceId) {
        if (window.SalesInvoiceFbr && window.SalesInvoiceFbr.downloadInvoicePdf) {
            window.SalesInvoiceFbr.downloadInvoicePdf(invoiceId);
            return;
        }
        window.open('/api/sales-invoices/' + invoiceId + '/pdf', '_blank');
    }

    function fetchInvoicePdfBlob(invoiceId) {
        return fetch('/api/sales-invoices/' + invoiceId + '/pdf')
            .then(function (response) {
                if (!response.ok) {
                    return response.json().then(function (body) {
                        throw new Error(body.message || body.Message || 'Could not load PDF.');
                    });
                }

                var disposition = response.headers.get('Content-Disposition') || '';
                var match = /filename="?([^";]+)"?/i.exec(disposition);
                var fileName = match ? match[1] : 'invoice.pdf';
                return response.blob().then(function (blob) {
                    return { blob: blob, fileName: fileName };
                });
            });
    }

    function shareViaWhatsApp() {
        if (!currentShareInfo) {
            return;
        }

        var phone = normalizeWhatsAppPhone($('#share-customer-whatsapp').val());
        var message = currentShareInfo.whatsAppMessage || '';
        var custom = $('#share-customer-message').val();
        if (custom && custom.trim()) {
            message = custom.trim();
        }

        var invoiceId = currentShareInfo.invoiceId;

        fetchInvoicePdfBlob(invoiceId)
            .then(function (pdf) {
                var file = new File([pdf.blob], pdf.fileName, { type: 'application/pdf' });
                if (navigator.canShare && navigator.canShare({ files: [file] })) {
                    return navigator.share({
                        files: [file],
                        title: pdf.fileName,
                        text: message
                    });
                }

                var url = window.URL.createObjectURL(pdf.blob);
                var link = document.createElement('a');
                link.href = url;
                link.download = pdf.fileName;
                document.body.appendChild(link);
                link.click();
                link.remove();
                window.URL.revokeObjectURL(url);

                var waUrl = phone
                    ? 'https://wa.me/' + phone + '?text=' + encodeURIComponent(message)
                    : 'https://wa.me/?text=' + encodeURIComponent(message);
                window.open(waUrl, '_blank');
                showShareAlert('success', 'PDF downloaded. Attach it in WhatsApp if not shared automatically.');
            })
            .catch(function (err) {
                showShareAlert('danger', err.message || 'Could not prepare WhatsApp share.');
            });
    }

    function sendEmail() {
        if (!currentShareInfo) {
            return;
        }

        var email = $('#share-customer-email').val();
        if (!email || !email.trim()) {
            showShareAlert('danger', 'Enter customer email address.');
            return;
        }

        var $btn = $('#btn-share-email');
        $btn.prop('disabled', true);
        showShareAlert(null, null);

        $.ajax({
            url: '/api/sales-invoices/' + currentShareInfo.invoiceId + '/email',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({
                toEmail: email.trim(),
                message: $('#share-customer-message').val() || null
            })
        })
            .done(function (result) {
                showShareAlert('success', result.message || 'Email sent successfully.');
            })
            .fail(function (xhr) {
                showShareAlert('danger', getApiErrorMessage(xhr, 'Failed to send email.'));
            })
            .always(function () {
                if (currentShareInfo && currentShareInfo.emailConfigured) {
                    $btn.prop('disabled', false);
                }
            });
    }

    function downloadDeliveryChallan(invoiceId) {
        window.open('/api/sales-invoices/' + invoiceId + '/delivery-challan-pdf', '_blank');
    }

    function sendChallanEmail() {
        if (!currentShareInfo) {
            return;
        }

        var email = $('#share-godown-email').val();
        if (!email || !email.trim()) {
            showShareAlert('danger', 'Enter godown email address.');
            return;
        }

        var $btn = $('#btn-share-email-challan');
        $btn.prop('disabled', true);
        showShareAlert(null, null);

        $.ajax({
            url: '/api/sales-invoices/' + currentShareInfo.invoiceId + '/email-challan',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({
                toEmail: email.trim(),
                message: $('#share-customer-message').val() || null
            })
        })
            .done(function (result) {
                showShareAlert('success', result.message || 'Delivery challan emailed to godown.');
            })
            .fail(function (xhr) {
                showShareAlert('danger', getApiErrorMessage(xhr, 'Failed to email delivery challan.'));
            })
            .always(function () {
                if (currentShareInfo && currentShareInfo.canEmailChallan && currentShareInfo.emailConfigured) {
                    $btn.prop('disabled', false);
                }
            });
    }

    function openShareModal(invoiceId, focusChallan) {
        var modal = ensureModal();
        if (!modal) {
            alert('Share dialog is not available on this page.');
            return;
        }

        showShareAlert(null, null);
        $('#share-invoice-summary').text('Loading...');

        $.getJSON('/api/sales-invoices/' + invoiceId + '/share-info')
            .done(function (info) {
                if (!info.canShare) {
                    alert('Invoice must be finalized (posted to GL and FBR-submitted, or posted for trade invoice) before sharing.');
                    return;
                }
                populateModal(info);
                modal.show();
                if (focusChallan) {
                    setTimeout(function () {
                        $('#share-godown-email').trigger('focus');
                    }, 300);
                }
            })
            .fail(function (xhr) {
                alert(getApiErrorMessage(xhr, 'Could not load share details.'));
            });
    }

    $(function () {
        $('#btn-share-email').on('click', sendEmail);
        $('#btn-share-whatsapp').on('click', shareViaWhatsApp);
        $('#btn-share-download-pdf').on('click', function () {
            if (currentShareInfo) {
                downloadInvoicePdf(currentShareInfo.invoiceId);
            }
        });
        $('#btn-share-download-challan').on('click', function () {
            if (currentShareInfo) {
                downloadDeliveryChallan(currentShareInfo.invoiceId);
            }
        });
        $('#btn-share-email-challan').on('click', sendChallanEmail);
    });

    window.SalesInvoiceShare = {
        openShareModal: openShareModal,
        openChallanShare: function (invoiceId) {
            openShareModal(invoiceId, true);
        },
        emailChallanToGodown: function (invoiceId) {
            showShareAlert(null, null);
            $.getJSON('/api/sales-invoices/' + invoiceId + '/share-info')
                .done(function (info) {
                    if (!info.canEmailChallan) {
                        alert('Delivery challan email to godown is only available for company 3 on posted invoices.');
                        return;
                    }
                    if (!info.emailConfigured) {
                        alert('SMTP is not configured. Set up email in appsettings.json first.');
                        return;
                    }

                    var email = info.godownEmail;
                    if (!email) {
                        if (window.SalesInvoiceShare.openChallanShare) {
                            window.SalesInvoiceShare.openChallanShare(invoiceId);
                        }
                        return;
                    }

                    if (!confirm('Email delivery challan to ' + email + '?')) {
                        return;
                    }

                    $.ajax({
                        url: '/api/sales-invoices/' + invoiceId + '/email-challan',
                        method: 'POST',
                        contentType: 'application/json',
                        data: JSON.stringify({ toEmail: email, message: null })
                    })
                        .done(function (result) {
                            alert(result.message || 'Delivery challan emailed to godown.');
                        })
                        .fail(function (xhr) {
                            alert(getApiErrorMessage(xhr, 'Failed to email delivery challan.'));
                        });
                })
                .fail(function (xhr) {
                    alert(getApiErrorMessage(xhr, 'Could not load invoice details.'));
                });
        }
    };
})(window);
