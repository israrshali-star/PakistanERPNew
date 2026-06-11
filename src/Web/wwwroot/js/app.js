(function ($) {
    'use strict';

    const App = {
        init: function () {
            this.initSidebar();
            this.loadCurrentCompany();
        },

        initSidebar: function () {
            const $nav = $('#topNav');
            const $overlay = $('#navOverlay');

            $('#navToggle').on('click', function () {
                $nav.toggleClass('show');
                $overlay.toggleClass('show');
            });

            $overlay.on('click', function () {
                $nav.removeClass('show');
                $overlay.removeClass('show');
            });

            $nav.find('a.nav-link:not(.top-nav-group-toggle), .top-nav-sublink').on('click', function () {
                if (window.innerWidth < 1200) {
                    $nav.removeClass('show');
                    $overlay.removeClass('show');
                }
            });

            $('.top-nav-group-label').on('click', function (e) {
                e.preventDefault();
            });
        },

        getCompanyDisplayName: function (company) {
            if (!company) {
                return '';
            }

            return company.companyName || company.CompanyName || '';
        },

        setCompanyDisplay: function (displayName, fallback) {
            const hasCompany = !!displayName;
            const headerName = hasCompany ? displayName : (fallback || 'PA ERP');

            $('#header-company-name').attr('title', headerName);
            $('#current-company-name').text(headerName);
            $('#app-brand-name').text(hasCompany ? displayName : 'PA ERP');
        },

        loadCurrentCompany: function () {
            App.setCompanyDisplay('', 'Loading...');

            $.getJSON('/api/company/current')
                .done(function (company) {
                    App.setCompanyDisplay(App.getCompanyDisplayName(company), 'PA ERP');
                })
                .fail(function () {
                    App.setCompanyDisplay('', 'Select company');
                    window.location.href = '/Account/SelectCompany';
                });
        },

        setCurrentCompanyName: function () {
            App.loadCurrentCompany();
        }
    };

    $(document).ready(function () {
        App.init();
    });

    window.ErpApp = App;
})(jQuery);
