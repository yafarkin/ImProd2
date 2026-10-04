// Экран команды (docs/manager-ui/README.md §8, блок 1): разделы живут на разных адресах, и переход
// «из панели внимания к фабрике» — это смена раздела, а не прокрутка внутри одной страницы. Встроенная
// прокрутка Blazor к #якорю срабатывает только в пределах того же маршрута, поэтому раздел после
// отрисовки сам просит браузер докрутить до нужного элемента.
window.teamScreen = {
    scrollToTop: function () {
        window.scrollTo(0, 0);
    },

    // Свёрнутые и раскрытые переделы «Производства» запоминаются в браузере (docs/manager-ui/README.md
    // §4): иначе каждый ход пришлось бы заново раскрывать нужное. Передел с проблемой
    // (data-force-open) раскрыт всегда — сохранённое «свёрнуто» не должно прятать фабрику, которой
    // сейчас плохо. Обработчик вешается один раз на элемент: функцию зовут после каждой отрисовки.
    rememberCollapsedGroups: function () {
        document.querySelectorAll('details[data-collapse-key]').forEach(function (element) {
            if (element.dataset.collapseBound) {
                return;
            }
            element.dataset.collapseBound = 'true';
            var key = 'team-collapse:' + element.dataset.collapseKey;
            try {
                var saved = localStorage.getItem(key);
                if (saved !== null && element.dataset.forceOpen !== 'true') {
                    element.open = saved === 'open';
                }
            } catch (e) {
                // Хранилище недоступно (приватный режим) — просто не запоминаем.
            }
            element.addEventListener('toggle', function () {
                try {
                    localStorage.setItem(key, element.open ? 'open' : 'closed');
                } catch (e) {
                    // См. выше.
                }
            });
        });
    },

    scrollToElement: function (elementId) {
        var element = document.getElementById(elementId);
        if (element) {
            element.scrollIntoView({ block: 'start', behavior: 'smooth' });
        }
    }
};
