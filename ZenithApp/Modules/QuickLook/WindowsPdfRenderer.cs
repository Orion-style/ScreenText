using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ZenithApp.Modules.QuickLook;

public sealed class WindowsPdfRenderer
{
    public readonly record struct RenderedPdfPage(BitmapSource Bitmap, double LogicalWidth, double LogicalHeight);

    public sealed class PdfSession : IDisposable
    {
        private readonly FileStream _fileStream;
        private readonly IRandomAccessStream _randomAccessStream;
        private readonly PdfDocument _document;

        private PdfSession(FileStream fileStream, IRandomAccessStream randomAccessStream, PdfDocument document)
        {
            _fileStream = fileStream;
            _randomAccessStream = randomAccessStream;
            _document = document;
        }

        public uint PageCount => _document.PageCount;

        public static async Task<PdfSession?> OpenAsync(string filePath)
        {
            FileStream? fs = null;
            IRandomAccessStream? ras = null;
            try
            {
                fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                ras = fs.AsRandomAccessStream();
                var doc = await PdfDocument.LoadFromStreamAsync(ras);
                return new PdfSession(fs, ras, doc);
            }
            catch
            {
                ras?.Dispose();
                fs?.Dispose();
                return null;
            }
        }

        public Task<RenderedPdfPage?> RenderPageAsync(
            uint pageIndex,
            uint maxWidth = 2600,
            uint maxHeight = 3800,
            uint targetWidth = 2200)
        {
            return RenderPageFromDocumentAsync(_document, pageIndex, maxWidth, maxHeight, targetWidth);
        }

        public void Dispose()
        {
            _randomAccessStream.Dispose();
            _fileStream.Dispose();
        }
    }

    private static async Task<RenderedPdfPage?> RenderPageFromDocumentAsync(
        PdfDocument pdfDoc,
        uint pageIndex,
        uint maxWidth,
        uint maxHeight,
        uint targetWidth)
    {
        try
        {
            if (pageIndex >= pdfDoc.PageCount) return null;

            using var page = pdfDoc.GetPage(pageIndex);
            var pageSize = page.Size;

            using var stream = new InMemoryRandomAccessStream();
            var renderOptions = new PdfPageRenderOptions
            {
                DestinationWidth = QuickLookLayoutHelper.ComputePdfRenderDestinationWidth(
                    pageSize.Width,
                    pageSize.Height,
                    maxWidth,
                    maxHeight,
                    targetWidth)
            };

            await page.RenderToStreamAsync(stream, renderOptions);

            var netStream = stream.AsStreamForRead();
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = netStream;
            bitmap.EndInit();
            bitmap.Freeze();

            double logicalW = pageSize.Width > 0 ? pageSize.Width : bitmap.PixelWidth;
            double logicalH = pageSize.Height > 0 ? pageSize.Height : bitmap.PixelHeight;
            return new RenderedPdfPage(bitmap, logicalW, logicalH);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<BitmapSource?> RenderPageAsync(
        string filePath,
        uint pageIndex,
        uint maxWidth = 2600,
        uint maxHeight = 3800,
        uint targetWidth = 2200)
    {
        try
        {
            using var session = await PdfSession.OpenAsync(filePath);
            if (session != null)
            {
                var rendered = await session.RenderPageAsync(pageIndex, maxWidth, maxHeight, targetWidth);
                return rendered?.Bitmap;
            }

            // Fallback to StorageFile if direct FileStream access fails
            var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
            var pdfDoc = await PdfDocument.LoadFromFileAsync(storageFile);
            var fallbackRendered = await RenderPageFromDocumentAsync(pdfDoc, pageIndex, maxWidth, maxHeight, targetWidth);
            return fallbackRendered?.Bitmap;
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
            using var session = await PdfSession.OpenAsync(filePath);
            if (session != null)
            {
                return session.PageCount;
            }

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
