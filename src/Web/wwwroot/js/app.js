(function ($) {
    'use strict';

    const App = {
        init: function () {
            this.initSidebar();
            this.loadCompanies();
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

        loadCompanies: function () {
            const $list = $('#company-list');
            const $name = $('#current-company-name');

            $.getJSON('/api/company/list')
                .done(function (companies) {
                    if (!companies || companies.length === 0) {
                        $name.text('No company');
                        return;
                    }

                    $list.empty();

                    companies.forEach(function (c) {
                        const $item = $('<li></li>');
                        const $link = $('<a class="dropdown-item" href="#"></a>')
                            .text(c.companyName)
                            .data('company-id', c.id)
                            .on('click', function (e) {
                                e.preventDefault();
                                App.switchCompany(c.id, c.companyName);
                            });

                        $item.append($link);
                        $list.append($item);
                    });

                    App.ensureCompanySelected(companies);
                })
                .fail(function () {
                    $name.text('Company');
                });
        },

        ensureCompanySelected: function (companies) {
            $.getJSON('/api/company/current')
                .done(function (company) {
                    $('#current-company-name').text(company.companyName);
                })
                .fail(function () {
                    if (!companies || companies.length === 0) {
                        $('#current-company-name').text('No company');
                        return;
                    }

                    $('#current-company-name').text('Select company');
                    window.location.href = '/Account/SelectCompany';
                });
        },

        setCurrentCompanyName: function () {
            $.getJSON('/api/company/current')
                .done(function (company) {
                    $('#current-company-name').text(company.companyName);
                })
                .fail(function () {
                    $('#current-company-name').text('Select company');
                });
        },

        switchCompany: function (companyId, companyName) {
            $.ajax({
                url: '/api/company/switch/' + companyId,
                method: 'POST'
            })
                .done(function () {
                    $('#current-company-name').text(companyName);
                    window.location.reload();
                })
                .fail(function () {
                    alert('Could not switch company. You may not have access.');
                });
        }
    };

    $(document).ready(function () {
        App.init();
    });

    window.ErpApp = App;
})(jQuery);
