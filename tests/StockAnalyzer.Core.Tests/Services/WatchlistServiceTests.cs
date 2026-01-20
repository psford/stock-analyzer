using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StockAnalyzer.Core.Models;
using StockAnalyzer.Core.Services;
using Xunit;

namespace StockAnalyzer.Core.Tests.Services;

public class WatchlistServiceTests
{
    private readonly Mock<IWatchlistRepository> _mockRepository;
    private readonly Mock<StockDataService> _mockStockDataService;
    private readonly Mock<ILogger<WatchlistService>> _mockLogger;
    private readonly WatchlistService _sut;

    public WatchlistServiceTests()
    {
        _mockRepository = new Mock<IWatchlistRepository>();
        _mockStockDataService = new Mock<StockDataService>();
        _mockLogger = new Mock<ILogger<WatchlistService>>();
        _sut = new WatchlistService(
            _mockRepository.Object,
            _mockStockDataService.Object,
            _mockLogger.Object);
    }

    #region GetAllAsync Tests

    [Fact]
    public async Task GetAllAsync_ReturnsAllWatchlists()
    {
        // Arrange
        var watchlists = new List<Watchlist>
        {
            CreateWatchlist("1", "Tech Stocks", new[] { "AAPL", "MSFT" }),
            CreateWatchlist("2", "Energy", new[] { "XOM" })
        };
        _mockRepository.Setup(r => r.GetAllAsync(null))
            .ReturnsAsync(watchlists);

        // Act
        var result = await _sut.GetAllAsync();

        // Assert
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Tech Stocks");
        result[1].Name.Should().Be("Energy");
    }

    [Fact]
    public async Task GetAllAsync_WithUserId_FiltersWatchlists()
    {
        // Arrange
        var watchlists = new List<Watchlist>
        {
            CreateWatchlist("1", "My Stocks", new[] { "AAPL" }, userId: "user1")
        };
        _mockRepository.Setup(r => r.GetAllAsync("user1"))
            .ReturnsAsync(watchlists);

        // Act
        var result = await _sut.GetAllAsync("user1");

        // Assert
        result.Should().HaveCount(1);
        _mockRepository.Verify(r => r.GetAllAsync("user1"), Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_WithNoWatchlists_ReturnsEmptyList()
    {
        // Arrange
        _mockRepository.Setup(r => r.GetAllAsync(null))
            .ReturnsAsync(new List<Watchlist>());

        // Act
        var result = await _sut.GetAllAsync();

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetByIdAsync Tests

    [Fact]
    public async Task GetByIdAsync_WithValidId_ReturnsWatchlist()
    {
        // Arrange
        var watchlist = CreateWatchlist("123", "Test", new[] { "AAPL" });
        _mockRepository.Setup(r => r.GetByIdAsync("123", null))
            .ReturnsAsync(watchlist);

        // Act
        var result = await _sut.GetByIdAsync("123");

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be("123");
        result.Name.Should().Be("Test");
    }

    [Fact]
    public async Task GetByIdAsync_WithInvalidId_ReturnsNull()
    {
        // Arrange
        _mockRepository.Setup(r => r.GetByIdAsync("invalid", null))
            .ReturnsAsync((Watchlist?)null);

        // Act
        var result = await _sut.GetByIdAsync("invalid");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_CreatesNewWatchlist()
    {
        // Arrange
        _mockRepository.Setup(r => r.CreateAsync(It.IsAny<Watchlist>()))
            .ReturnsAsync((Watchlist w) => w with { Id = "new-id" });

        // Act
        var result = await _sut.CreateAsync("My Watchlist");

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("My Watchlist");
        result.Tickers.Should().BeEmpty();
        _mockRepository.Verify(r => r.CreateAsync(It.Is<Watchlist>(w =>
            w.Name == "My Watchlist" &&
            w.Tickers.Count == 0 &&
            w.UserId == null
        )), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithUserId_SetsUserId()
    {
        // Arrange
        _mockRepository.Setup(r => r.CreateAsync(It.IsAny<Watchlist>()))
            .ReturnsAsync((Watchlist w) => w with { Id = "new-id" });

        // Act
        var result = await _sut.CreateAsync("My Watchlist", "user123");

        // Assert
        _mockRepository.Verify(r => r.CreateAsync(It.Is<Watchlist>(w =>
            w.UserId == "user123"
        )), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_TrimsWhitespace()
    {
        // Arrange
        _mockRepository.Setup(r => r.CreateAsync(It.IsAny<Watchlist>()))
            .ReturnsAsync((Watchlist w) => w with { Id = "new-id" });

        // Act
        var result = await _sut.CreateAsync("  My Watchlist  ");

        // Assert
        result.Name.Should().Be("My Watchlist");
    }

    #endregion

    #region RenameAsync Tests

    [Fact]
    public async Task RenameAsync_WithValidId_RenamesWatchlist()
    {
        // Arrange
        var existingWatchlist = CreateWatchlist("123", "Old Name", new[] { "AAPL" });
        _mockRepository.Setup(r => r.GetByIdAsync("123", null))
            .ReturnsAsync(existingWatchlist);
        _mockRepository.Setup(r => r.UpdateAsync(It.IsAny<Watchlist>()))
            .ReturnsAsync((Watchlist w) => w);

        // Act
        var result = await _sut.RenameAsync("123", "New Name");

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("New Name");
        _mockRepository.Verify(r => r.UpdateAsync(It.Is<Watchlist>(w =>
            w.Id == "123" && w.Name == "New Name"
        )), Times.Once);
    }

    [Fact]
    public async Task RenameAsync_WithInvalidId_ReturnsNull()
    {
        // Arrange
        _mockRepository.Setup(r => r.GetByIdAsync("invalid", null))
            .ReturnsAsync((Watchlist?)null);

        // Act
        var result = await _sut.RenameAsync("invalid", "New Name");

        // Assert
        result.Should().BeNull();
        _mockRepository.Verify(r => r.UpdateAsync(It.IsAny<Watchlist>()), Times.Never);
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_WithValidId_ReturnsTrue()
    {
        // Arrange
        _mockRepository.Setup(r => r.DeleteAsync("123", null))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.DeleteAsync("123");

        // Assert
        result.Should().BeTrue();
        _mockRepository.Verify(r => r.DeleteAsync("123", null), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WithInvalidId_ReturnsFalse()
    {
        // Arrange
        _mockRepository.Setup(r => r.DeleteAsync("invalid", null))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.DeleteAsync("invalid");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region AddTickerAsync Tests

    [Fact]
    public async Task AddTickerAsync_WithValidId_AddsTicker()
    {
        // Arrange
        var updatedWatchlist = CreateWatchlist("123", "Test", new[] { "AAPL", "MSFT" });
        _mockRepository.Setup(r => r.AddTickerAsync("123", "MSFT", null))
            .ReturnsAsync(updatedWatchlist);

        // Act
        var result = await _sut.AddTickerAsync("123", "MSFT");

        // Assert
        result.Should().NotBeNull();
        result!.Tickers.Should().Contain("MSFT");
        _mockRepository.Verify(r => r.AddTickerAsync("123", "MSFT", null), Times.Once);
    }

    [Fact]
    public async Task AddTickerAsync_WithInvalidId_ReturnsNull()
    {
        // Arrange
        _mockRepository.Setup(r => r.AddTickerAsync("invalid", "AAPL", null))
            .ReturnsAsync((Watchlist?)null);

        // Act
        var result = await _sut.AddTickerAsync("invalid", "AAPL");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region RemoveTickerAsync Tests

    [Fact]
    public async Task RemoveTickerAsync_WithValidIdAndTicker_RemovesTicker()
    {
        // Arrange
        var updatedWatchlist = CreateWatchlist("123", "Test", new[] { "AAPL" });
        _mockRepository.Setup(r => r.RemoveTickerAsync("123", "MSFT", null))
            .ReturnsAsync(updatedWatchlist);

        // Act
        var result = await _sut.RemoveTickerAsync("123", "MSFT");

        // Assert
        result.Should().NotBeNull();
        result!.Tickers.Should().NotContain("MSFT");
        _mockRepository.Verify(r => r.RemoveTickerAsync("123", "MSFT", null), Times.Once);
    }

    [Fact]
    public async Task RemoveTickerAsync_WithInvalidId_ReturnsNull()
    {
        // Arrange
        _mockRepository.Setup(r => r.RemoveTickerAsync("invalid", "AAPL", null))
            .ReturnsAsync((Watchlist?)null);

        // Act
        var result = await _sut.RemoveTickerAsync("invalid", "AAPL");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetQuotesAsync Tests

    [Fact]
    public async Task GetQuotesAsync_WithInvalidId_ReturnsNull()
    {
        // Arrange
        _mockRepository.Setup(r => r.GetByIdAsync("invalid", null))
            .ReturnsAsync((Watchlist?)null);

        // Act
        var result = await _sut.GetQuotesAsync("invalid");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetQuotesAsync_WithEmptyWatchlist_ReturnsEmptyQuotes()
    {
        // Arrange
        var watchlist = CreateWatchlist("123", "Empty", Array.Empty<string>());
        _mockRepository.Setup(r => r.GetByIdAsync("123", null))
            .ReturnsAsync(watchlist);

        // Act
        var result = await _sut.GetQuotesAsync("123");

        // Assert
        result.Should().NotBeNull();
        result!.Quotes.Should().BeEmpty();
        result.WatchlistId.Should().Be("123");
        result.WatchlistName.Should().Be("Empty");
    }

    #endregion

    #region Helper Methods

    private static Watchlist CreateWatchlist(
        string id,
        string name,
        string[] tickers,
        string? userId = null)
    {
        return new Watchlist
        {
            Id = id,
            Name = name,
            Tickers = tickers.ToList(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            UserId = userId
        };
    }

    #endregion
}
