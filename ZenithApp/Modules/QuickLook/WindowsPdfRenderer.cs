using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;

namespace ZenithApp.Modules.QuickLook;

public sealed class WindowsPdfRenderer
{
    public static async Task<BitmapSource?> RenderPageAsync(string filePath, uint pageIndex, uint maxWidth = 1600)
    {
        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
            var pdfDoc = await PdfDocument.LoadFromFileAsync(storageFile);
            if (pageIndex >= pdfDoc.PageCount) return null;

            using var page = pdfDoc.GetPage(pageIndex);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();

            var renderOptions = new PdfPageRenderOptions();
            var pageSize = page.Size;
            if (pageSize.Width > maxWidth)
            {
                renderOptions.DestinationWidth = maxWidth;
            }

            await page.RenderToStreamAsync(stream, renderOptions);

            var netStream = stream.AsStreamForRead();
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = netStream;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<uint> GetPageCountAsync(string filePath)
    {
        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
            var pdfDoc = await PdfDocument.LoadFromFileAsync(storageFile);
            return pdfDoc.PageCount;
        }
        catch
        {
            return 0;
        }
    }
}
