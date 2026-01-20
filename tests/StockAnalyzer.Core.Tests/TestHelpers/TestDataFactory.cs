using StockAnalyzer.Core.Models;

namespace StockAnalyzer.Core.Tests.TestHelpers;

/// <summary>
/// Factory methods for creating test data.
/// </summary>
public static class TestDataFactory
{
    /// <summary>
    /// Creates a list of OHLCV data points with predictable values.
    /// </summary>
    /// <param name="count">Number of data points to create.</param>
    /// <param name="startPrice">Starting close price.</param>
    /// <param name="startDate">Starting date (defaults to today minus count days).</param>
    /// <returns>List of OhlcvData.</returns>
    public static List<OhlcvData> CreateOhlcvDataList(
        int count,
        decimal startPrice = 100m,
        DateTime? startDate = null)
    {
        var date = startDate ?? DateTime.Today.AddDays(-count);
        var data = new List<OhlcvData>();

        for (int i = 0; i < count; i++)
        {
            // Create slightly varying prices (small daily changes)
            var close = startPrice + (i * 0.5m);
            var open = close - 0.25m;
            var high = close + 0.5m;
            var low = open - 0.25m;

            data.Add(new OhlcvData
            {
                Date = date.AddDays(i),
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = 1000000 + (i * 10000)
            });
        }

        return data;
    }

    /// <summary>
    /// Creates a single OHLCV data point with specified values.
    /// </summary>
    public static OhlcvData CreateOhlcvData(
        DateTime date,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long volume = 1000000)
    {
        return new OhlcvData
        {
            Date = date,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = volume
        };
    }

    /// <summary>
    /// Creates OHLCV data with a significant move on a specific day.
    /// </summary>
    /// <param name="count">Total days of data.</param>
    /// <param name="significantMoveDay">Day index (0-based) for the significant move.</param>
    /// <param name="percentChange">Percent change for the significant move.</param>
    public static List<OhlcvData> CreateOhlcvDataWithSignificantMove(
        int count,
        int significantMoveDay,
        decimal percentChange)
    {
        var data = CreateOhlcvDataList(count, 100m);

        if (significantMoveDay >= 0 && significantMoveDay < count)
        {
            var basePrice = data[significantMoveDay].Open;
            var newClose = basePrice * (1 + percentChange / 100);

            data[significantMoveDay] = data[significantMoveDay] with
            {
                Close = newClose,
                High = Math.Max(data[significantMoveDay].High, newClose)
            };
        }

        return data;
    }

    /// <summary>
    /// Creates a list of news items.
    /// </summary>
    public static List<NewsItem> CreateNewsItemList(int count)
    {
        var news = new List<NewsItem>();
        var baseDate = DateTime.Today;

        for (int i = 0; i < count; i++)
        {
            news.Add(new NewsItem
            {
                Headline = $"Test Headline {i + 1}",
                Summary = $"Test summary for news item {i + 1}",
                Source = $"Test Source {i % 3 + 1}",
                PublishedAt = baseDate.AddHours(-i),
                Url = $"https://example.com/news/{i + 1}",
                ImageUrl = $"https://example.com/images/{i + 1}.jpg",
                Category = "company",
                RelatedSymbols = new List<string> { "AAPL", "MSFT" }
            });
        }

        return news;
    }

    /// <summary>
    /// Creates a single news item.
    /// </summary>
    public static NewsItem CreateNewsItem(
        string headline = "Test Headline",
        string source = "Test Source",
        DateTime? publishedAt = null)
    {
        return new NewsItem
        {
            Headline = headline,
            Summary = "Test summary",
            Source = source,
            PublishedAt = publishedAt ?? DateTime.Today,
            Url = "https://example.com/news",
            ImageUrl = "https://example.com/image.jpg"
        };
    }

    /// <summary>
    /// Creates a significant move record.
    /// </summary>
    public static SignificantMove CreateSignificantMove(
        decimal percentChange,
        DateTime? date = null,
        List<NewsItem>? relatedNews = null)
    {
        var openPrice = 100m;
        var closePrice = openPrice * (1 + percentChange / 100);

        return new SignificantMove
        {
            Date = date ?? DateTime.Today,
            OpenPrice = openPrice,
            ClosePrice = closePrice,
            PercentChange = percentChange,
            Volume = 5000000,
            RelatedNews = relatedNews
        };
    }

    /// <summary>
    /// Creates a HistoricalDataResult with the given data.
    /// </summary>
    public static HistoricalDataResult CreateHistoricalDataResult(
        string symbol = "TEST",
        string period = "1y",
        List<OhlcvData>? data = null)
    {
        var ohlcvData = data ?? CreateOhlcvDataList(30);

        return new HistoricalDataResult
        {
            Symbol = symbol,
            Period = period,
            StartDate = ohlcvData.First().Date,
            EndDate = ohlcvData.Last().Date,
            Data = ohlcvData
        };
    }

    /// <summary>
    /// Creates a NewsResult with the given articles.
    /// </summary>
    public static NewsResult CreateNewsResult(
        string symbol = "TEST",
        List<NewsItem>? articles = null)
    {
        var newsItems = articles ?? CreateNewsItemList(5);

        return new NewsResult
        {
            Symbol = symbol,
            FromDate = DateTime.Today.AddDays(-30),
            ToDate = DateTime.Today,
            Articles = newsItems
        };
    }

    /// <summary>
    /// Creates a SignificantMovesResult with the given moves.
    /// </summary>
    public static SignificantMovesResult CreateSignificantMovesResult(
        string symbol = "TEST",
        decimal threshold = 5m,
        List<SignificantMove>? moves = null)
    {
        return new SignificantMovesResult
        {
            Symbol = symbol,
            Threshold = threshold,
            Moves = moves ?? new List<SignificantMove>
            {
                CreateSignificantMove(6.5m),
                CreateSignificantMove(-5.2m),
                CreateSignificantMove(8.0m)
            }
        };
    }
}
