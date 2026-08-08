// Свой обработчик обрыва circuit вместо штатного (расследование зависаний интерфейса,
// project_ui_freeze_investigation, 2026-08-08) — штатный баннер «Attempting to reconnect» надёжно
// показывается в Chrome при реальном обрыве (проверено), но у пользователя не показывается в
// Firefox при том же самом обрыве. reconnectionHandler — официальный, документированный способ
// Blazor подписаться на обрыв/восстановление circuit (см. `Blazor.start({ circuit: {
// reconnectionHandler } })`); ставим свой не чтобы заменить штатное поведение навсегда, а чтобы
// узнать: сам клиентский код Blazor вообще замечает обрыв в Firefox (тогда просто не отрисовался
// штатный UI — чинится легко) или обрыв не долетает до него вовсе (проблема глубже). Лог идёт под
// тем же тегом [diag], что и diagnostics.js, — читать оба лога вместе. Баннер показываем всегда, не
// только при включённой диагностике, — это реальная недостающая обратная связь пользователю, а не
// просто отладочный шум.
(function () {
    'use strict';

    function log(message) {
        console.log('[diag]', message, 'at=', new Date().toISOString());
    }

    function ensureOverlay() {
        var overlay = document.getElementById('diag-reconnect-overlay');
        if (overlay) {
            return overlay;
        }

        overlay = document.createElement('div');
        overlay.id = 'diag-reconnect-overlay';
        overlay.style.cssText =
            'position:fixed;top:0;left:0;right:0;padding:0.6rem 1rem;background:#b32121;color:#fff;' +
            'font:14px sans-serif;text-align:center;z-index:99999;display:none;';
        overlay.textContent = 'Соединение с сервером потеряно — пробуем переподключиться...';
        document.body.appendChild(overlay);
        return overlay;
    }

    var handler = {
        onConnectionDown: function (options, error) {
            log('Blazor onConnectionDown: ' + (error ? (error.message || error) : '(без деталей ошибки)'));
            ensureOverlay().style.display = 'block';
        },
        onConnectionUp: function () {
            log('Blazor onConnectionUp');
            ensureOverlay().style.display = 'none';
        },
    };

    Blazor.start({
        circuit: {
            reconnectionHandler: handler,
        },
    });
})();
