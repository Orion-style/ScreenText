using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Windows.Media.Ocr;

namespace ZenithApp.Modules.Screenshot;

public static class OcrLayoutReconstructor
{
    public sealed class WordBox
    {
        public string Text { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public static string ReconstructText(OcrResult ocrResult)
    {
        if (ocrResult == null || ocrResult.Lines == null || ocrResult.Lines.Count == 0)
        {
            return ocrResult?.Text?.Trim() ?? string.Empty;
        }

        // Извлекаем все слова с их точными координатами
        var words = new List<WordBox>();
        foreach (var line in ocrResult.Lines)
        {
            foreach (var word in line.Words)
            {
                if (!string.IsNullOrWhiteSpace(word.Text))
                {
                    words.Add(new WordBox
                    {
                        Text = word.Text,
                        X = word.BoundingRect.X,
                        Y = word.BoundingRect.Y,
                        Width = word.BoundingRect.Width,
                        Height = word.BoundingRect.Height
                    });
                }
            }
        }

        if (words.Count == 0)
        {
            return ocrResult.Text?.Trim() ?? string.Empty;
        }

        // Вычисляем среднюю высоту слова для определения допуска вертикальной строки
        double avgHeight = words.Average(w => w.Height);
        double lineThreshold = Math.Max(6.0, avgHeight * 0.65);

        // Группируем слова в строки по вертикальной близости (Y)
        // Сортируем слова сначала по Y
        var sortedWords = words.OrderBy(w => w.Y).ToList();
        var visualLines = new List<List<WordBox>>();

        foreach (var word in sortedWords)
        {
            // Ищем строку, в которую слово укладывается по высоте
            var targetLine = visualLines.FirstOrDefault(line =>
            {
                double lineAvgY = line.Average(w => w.Y);
                return Math.Abs(word.Y - lineAvgY) <= lineThreshold;
            });

            if (targetLine != null)
            {
                targetLine.Add(word);
            }
            else
            {
                visualLines.Add(new List<WordBox> { word });
            }
        }

        // Сортируем строки сверху вниз (по среднему Y)
        visualLines = visualLines.OrderBy(line => line.Average(w => w.Y)).ToList();

        // Внутри каждой строки сортируем слова строго слева направо (по X)
        var lineStrings = new List<string>();
        foreach (var line in visualLines)
        {
            var orderedLineWords = line.OrderBy(w => w.X).Select(w => w.Text);
            var lineText = string.Join(" ", orderedLineWords).Trim();
            if (!string.IsNullOrEmpty(lineText))
            {
                lineStrings.Add(lineText);
            }
        }

        // Соединяем строки через пробел или перевод строки (для предложений в абзаце)
        return string.Join(" ", lineStrings);
    }
}
