(function (window) {
    'use strict';

    function shareRemoteFile(url, fileName) {
        if (!url) {
            return;
        }

        fetch(url)
            .then(function (response) {
                if (!response.ok) {
                    throw new Error('Could not load the document.');
                }
                var disposition = response.headers.get('Content-Disposition') || '';
                var match = /filename\*?=(?:UTF-8''|")?([^";]+)/i.exec(disposition);
                var name = fileName || (match ? decodeURIComponent(match[1].replace(/"/g, '')) : 'document');
                return response.blob().then(function (blob) {
                    return { blob: blob, fileName: name };
                });
            })
            .then(function (file) {
                var shareFile = new File([file.blob], file.fileName, { type: file.blob.type || 'application/octet-stream' });
                if (navigator.canShare && navigator.canShare({ files: [shareFile] })) {
                    return navigator.share({
                        files: [shareFile],
                        title: file.fileName
                    });
                }

                var objectUrl = window.URL.createObjectURL(file.blob);
                var link = document.createElement('a');
                link.href = objectUrl;
                link.download = file.fileName;
                document.body.appendChild(link);
                link.click();
                link.remove();
                window.URL.revokeObjectURL(objectUrl);
                alert('Document downloaded. Attach it in WhatsApp or email to share.');
            })
            .catch(function (err) {
                alert(err.message || 'Could not share the document.');
            });
    }

    $(document).on('click', '.js-share-invoice', function (e) {
        e.preventDefault();
        var id = $(this).data('id');
        if (window.SalesInvoiceShare) {
            window.SalesInvoiceShare.openShareModal(id);
        }
    });

    $(document).on('click', '.js-share-receipt', function (e) {
        e.preventDefault();
        var id = $(this).data('id');
        if (window.ReceiptShare) {
            window.ReceiptShare.open(id);
        }
    });

    $(document).on('click', '.js-print-receipt', function (e) {
        e.preventDefault();
        var id = $(this).data('id');
        if (window.ReceiptShare && window.ReceiptShare.print) {
            window.ReceiptShare.print(id);
            return;
        }
        window.open('/api/customer-receipts/' + id + '/pdf', '_blank');
    });

    $(document).on('click', '.js-share-vendor-payment', function (e) {
        e.preventDefault();
        var id = $(this).data('id');
        if (window.VendorPaymentShare) {
            window.VendorPaymentShare.open(id);
        }
    });

    $(document).on('click', '.js-share-file', function (e) {
        e.preventDefault();
        shareRemoteFile($(this).data('url'), $(this).data('name'));
    });
})(window);
