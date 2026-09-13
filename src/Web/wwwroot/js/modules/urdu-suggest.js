(function () {
    'use strict';

    var URDU_COMPANY_ID = 3;

    function supportsCompany(companyId) {
        return parseInt(companyId, 10) === URDU_COMPANY_ID;
    }

    function fetchScript(text) {
        if (!text || !String(text).trim()) {
            return $.Deferred().resolve('').promise();
        }

        return $.getJSON('/api/lookup/urdu-script', { text: String(text).trim() })
            .then(function (result) {
                return (result && (result.text || result.Text)) || '';
            }, function () {
                return '';
            });
    }

    function bindAutoFill(options) {
        var $english = $(options.englishSelector);
        var $urdu = $(options.urduSelector);
        var getCompanyId = options.getCompanyId || function () { return 0; };
        var timer = null;
        var touched = false;
        var lastRequest = 0;

        function suggest() {
            if (!supportsCompany(getCompanyId()) || touched) {
                return;
            }

            var requestId = ++lastRequest;
            fetchScript($english.val()).done(function (urdu) {
                if (requestId !== lastRequest || touched) {
                    return;
                }

                $urdu.val(urdu);
            });
        }

        $english.on('input', function () {
            clearTimeout(timer);
            timer = setTimeout(suggest, 250);
        });

        $urdu.on('input', function () {
            touched = true;
        });

        return {
            markPristine: function () {
                touched = false;
            },
            markTouchedIfFilled: function () {
                touched = !!($urdu.val() && $urdu.val().trim());
            },
            suggest: suggest
        };
    }

    window.UrduSuggest = {
        supportsCompany: supportsCompany,
        fetchScript: fetchScript,
        bindAutoFill: bindAutoFill
    };
})();
