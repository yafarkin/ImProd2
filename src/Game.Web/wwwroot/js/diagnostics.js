// Временная диагностика подвисания интерфейса (запрос пользователя, 2026-08-08): три независимых
// сигнала в консоль, чтобы понять, что именно стоит — вкладка браузера (JS event loop не крутится,
// например из-за троттлинга при энергосбережении/фоновой вкладке) или соединение с сервером
// (SignalR circuit жив, но подвисла его собственная очередь обработки — тогда обычный fetch мимо
// circuit всё равно быстро отвечает). Включается не по умолчанию, чтобы не шуметь в консоли на
// реальном мероприятии — добавьте ?diag=1 к адресу страницы или один раз выполните в консоли
// `localStorage.diag = '1'` и перезагрузите страницу. Выключить — `localStorage.removeItem('diag')`.
// Убрать этот файл и его подключение в App.razor, когда причина найдена — не постоянная часть приложения.
(function () {
    'use strict';

    var enabled = new URLSearchParams(location.search).has('diag') || localStorage.getItem('diag') === '1';
    if (!enabled) {
        return;
    }

    var tag = '[diag]';
    console.log(tag, 'включена, старт', new Date().toISOString(), 'hidden=', document.hidden);

    // Сигнал 1: дрейф таймера самой вкладки. Планируем каждый следующий тик через ровно 1000 мс от
    // предыдущего запуска; если реальный интервал заметно больше — значит браузер/ОС придержали
    // выполнение JS этой вкладки (энергосбережение, App Nap, фоновая вкладка), а не сервер тормозит.
    var lastTick = performance.now();
    setInterval(function () {
        var now = performance.now();
        var drift = now - lastTick;
        lastTick = now;
        var log = drift > 1500 ? console.warn : console.log;
        log(tag, 'tick drift=' + drift.toFixed(0) + 'мс hidden=' + document.hidden + ' at=' + new Date().toISOString());
    }, 1000);

    // Сигнал 2: смена видимости вкладки — привязать моменты подвисания к сворачиванию/переключению.
    document.addEventListener('visibilitychange', function () {
        console.log(tag, 'visibilitychange ->', document.visibilityState, new Date().toISOString());
    });

    // Сигнал 3: реальная доступность сервера — обычный fetch, идёт мимо Blazor circuit и его
    // внутренней очереди рендера. Если он отвечает быстро, пока страница выглядит подвисшей — дело
    // не в сети и не в сервере, а в застрявшей очереди обработки конкретно этого circuit.
    setInterval(function () {
        var start = performance.now();
        fetch(location.href, { method: 'HEAD', cache: 'no-store' })
            .then(function (response) {
                var ms = (performance.now() - start).toFixed(0);
                console.log(tag, 'ping ok status=' + response.status + ' ' + ms + 'мс at=' + new Date().toISOString());
            })
            .catch(function (error) {
                console.error(tag, 'ping FAILED', error, 'at=', new Date().toISOString());
            });
    }, 2000);
})();
