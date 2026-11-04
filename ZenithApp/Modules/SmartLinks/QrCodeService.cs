using System.Drawing;
using ZXing;
using ZXing.Windows.Compatibility;

namespace ZenithApp.Modules.SmartLinks;

public static class QrCodeService
{
    public static string? TryDecodeQrCode(Bitmap bitmap)
    {
        try
        {
            var reader = new BarcodeReader
            {
                AutoRotate = true,
                Options = new ZXing.Common.DecodingOptions
                {
                    TryHarder = true,
                    PossibleFormats = new[] { BarcodeFormat.QR_CODE }
                }
            };

            var result = reader.Decode(bitmap);
            return result?.Text?.Trim();
        }
        catch
        {
            return null;
        }
    }
}
