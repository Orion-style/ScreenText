using System.IO;
using ZenithApp.Modules.T9Predictor;

namespace ZenithApp.Tests;

public class T9DictionaryTests
{
    [Fact]
    public void DictionaryBinaryBuilder_CreatesReadableMemoryMappedDictionary()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "test_dict_" + Guid.NewGuid() + ".dict");
        try
        {
            var words = new (string, uint)[]
            {
                ("привет", 90000),
                ("приветствую", 40000),
                ("пример", 85000),
                ("программа", 99000),
                ("application", 95000),
                ("apple", 80000)
            };

            DictionaryBinaryBuilder.BuildBinaryFile(words, tempFile);

            using var dict = new MemoryMappedDictionary(tempFile, "ru");

            // Test prefix lookup
            var match1 = dict.FindBestMatch("прив");
            Assert.Equal("привет", match1);

            var match2 = dict.FindBestMatch("приме");
            Assert.Equal("пример", match2);

            var match3 = dict.FindBestMatch("app");
            Assert.Equal("application", match3); // Higher frequency than apple

            var notFound = dict.FindBestMatch("xyz");
            Assert.Null(notFound);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
