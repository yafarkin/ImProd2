// Экран команды (docs/manager-ui/README.md §8, блок 1): разделы живут на разных адресах, и переход
// «из панели внимания к фабрике» — это смена раздела, а не прокрутка внутри одной страницы. Встроенная
// прокрутка Blazor к #якорю срабатывает только в пределах того же маршрута, поэтому раздел после
// отрисовки сам просит браузер докрутить до нужного элемента.
window.teamScreen = {
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
