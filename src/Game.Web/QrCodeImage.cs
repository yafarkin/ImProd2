using QRCoder;

namespace Game.Web;

/// <summary>Рендер QR-кода как data URL картинки (SPEC §3 doc-comment — «отдельная, более дешёвая надстройка») — общая точка для `/join` и QR конкретного участника.</summary>
public static class QrCodeImage
{
    public static string ToDataUrl(string content, int pixelsPerModule = 14)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule);
        return $"data:image/png;base64,{Convert.ToBase64String(png)}";
    }
}
