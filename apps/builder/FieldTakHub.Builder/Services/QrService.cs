using QRCoder;
namespace FieldTakHub.Builder.Services;
public sealed class QrService
{
    public byte[] CreatePng(string value)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(12);
    }
}
