namespace Game.Web;

/// <summary>
/// Метка сборки для ссылок на стили и скрипты (<c>app.css?v=…</c>). Без неё телефон, заходивший на
/// прошлую сборку, держал старый файл стилей из кеша и показывал новую разметку без оформления
/// (живая проверка блока 1 редизайна, 2026-10-04). Идентификатор модуля меняется при каждой
/// компиляции, поэтому новая сборка на сервере — это новые адреса файлов, и браузер скачивает их заново.
/// </summary>
public static class AssetVersion
{
    public static string Value { get; } = typeof(AssetVersion).Assembly.ManifestModule.ModuleVersionId.ToString("N")[..8];
}
