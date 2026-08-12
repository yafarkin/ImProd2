namespace Game.Web;

/// <summary>
/// Отладочный режим (конфиг <c>DebugMode</c> в <c>appsettings.json</c>, по умолчанию выключен) — на
/// реальном мероприятии должен быть <see langword="false"/>. Пока включён, <see
/// cref="Components.Shared.DebugBanner"/> рисует яркую полосу сверху на каждой странице и даёт
/// переключиться на любого известного участника без ввода кода — быстрая проверка ролей при
/// разработке, не для использования во время игры с реальными участниками.
/// </summary>
public sealed record DebugModeState(bool Enabled);
