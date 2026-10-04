// Экран команды (docs/manager-ui/README.md §8, блок 1): разделы живут на разных адресах, и переход
// «из панели внимания к фабрике» — это смена раздела, а не прокрутка внутри одной страницы. Встроенная
// прокрутка Blazor к #якорю срабатывает только в пределах того же маршрута, поэтому раздел после
// отрисовки сам просит браузер докрутить до нужного элемента.
window.teamScreen = {
    restoreProductionGroups: function () {
        document.querySelectorAll('[data-collapse-key]').forEach(function (element) {
            if (element.dataset.collapseBound) return;
            element.dataset.collapseBound = 'true';
            const key = 'production:' + element.dataset.collapseKey;
            try {
                const saved = localStorage.getItem(key);
                if (saved !== null) element.open = saved === 'true';
            } catch (_) { }
            element.addEventListener('toggle', function () {
                try { localStorage.setItem(key, String(element.open)); } catch (_) { }
            });
        });
    },
    scrollToTop: function () {
        window.scrollTo(0, 0);
    },

    scrollToElement: function (elementId) {
        var element = document.getElementById(elementId);
        if (element) {
            element.scrollIntoView({ block: 'start', behavior: 'smooth' });
        }
    }
};
