using Xunit;
using ZenithApp.Modules.Screenshot;

namespace ZenithApp.Tests;

public class OcrHistoryServiceTests
{
    [Fact]
    public void Add_ValidTexts_StoredInLruOrder()
    {
        var service = new OcrHistoryService();

        service.Add("First");
        service.Add("Second");
        service.Add("Third");

        var history = service.GetHistory();
        Assert.Equal(3, history.Count);
        Assert.Equal("Third", history[0]);
        Assert.Equal("Second", history[1]);
        Assert.Equal("First", history[2]);
    }

    [Fact]
    public void Add_Duplicates_MovesToTopWithoutDuplication()
    {
        var service = new OcrHistoryService();

        service.Add("Alpha");
        service.Add("Beta");
        service.Add("Alpha");

        var history = service.GetHistory();
        Assert.Equal(2, history.Count);
        Assert.Equal("Alpha", history[0]);
        Assert.Equal("Beta", history[1]);
    }

    [Fact]
    public void Add_ExceedingLimit_TruncatesTo10()
    {
        var service = new OcrHistoryService();

        for (int i = 0; i < 15; i++)
        {
            service.Add($"Item {i}");
        }

        var history = service.GetHistory();
        Assert.Equal(10, history.Count);
        Assert.Equal("Item 14", history[0]);
        Assert.Equal("Item 5", history[9]);
    }

    [Fact]
    public void Clear_EmptiesAllItems()
    {
        var service = new OcrHistoryService();
        service.Add("Item 1");
        service.Add("Item 2");

        Assert.Equal(2, service.GetHistory().Count);

        service.Clear();
        Assert.Empty(service.GetHistory());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Add_EmptyOrWhitespace_IsIgnored(string? emptyText)
    {
        var service = new OcrHistoryService();
        service.Add(emptyText!);

        Assert.Empty(service.GetHistory());
    }
}
