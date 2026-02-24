namespace StockAnalyzer.Core.Tests.Data;

using FluentAssertions;
using StockAnalyzer.Core.Data;
using StockAnalyzer.Core.Services;
using Xunit;

/// <summary>
/// Unit tests for coverage delta computation in SqlPriceRepository.
/// Tests pure in-memory delta calculation from newly inserted prices.
/// Verifies AC1.1 and AC1.6 (per-security coverage metadata incremental updates).
/// </summary>
public class SqlPriceRepositoryCoverageTests
{
    [Fact]
    public void ComputeDeltas_EmptyInput_ReturnsEmptyList()
    {
        // Arrange
        var newPrices = new List<PriceCreateDto>();

        // Act
        var deltas = SqlPriceRepository.ComputeDeltas(newPrices);

        // Assert
        deltas.Should().BeEmpty();
    }

    [Fact]
    public void ComputeDeltas_SinglePrice_ProducesCorrectDelta()
    {
        // Arrange
        var date = new DateTime(2024, 1, 15);
        var newPrices = new List<PriceCreateDto>
        {
            new()
            {
                SecurityAlias = 1,
                EffectiveDate = date,
                Open = 100m,
                High = 105m,
                Low = 99m,
                Close = 102m,
                Volatility = 0.01m,
                Volume = 1000000,
                AdjustedClose = 102m
            }
        };

        // Act
        var deltas = SqlPriceRepository.ComputeDeltas(newPrices);

        // Assert
        deltas.Should().HaveCount(1);
        var delta = deltas[0];
        delta.SecurityAlias.Should().Be(1);
        delta.InsertedCount.Should().Be(1);
        delta.MinDate.Should().Be(date.Date);
        delta.MaxDate.Should().Be(date.Date);
        delta.YearCounts.Should().ContainKey(2024).WhoseValue.Should().Be(1);
    }

    [Fact]
    public void ComputeDeltas_MultipleForOneSecurity_CorrectCountAndRange()
    {
        // Arrange
        var date1 = new DateTime(2024, 1, 15);
        var date2 = new DateTime(2024, 1, 16);
        var date3 = new DateTime(2024, 1, 17);

        var newPrices = new List<PriceCreateDto>
        {
            CreatePrice(1, date1),
            CreatePrice(1, date2),
            CreatePrice(1, date3)
        };

        // Act
        var deltas = SqlPriceRepository.ComputeDeltas(newPrices);

        // Assert
        deltas.Should().HaveCount(1);
        var delta = deltas[0];
        delta.SecurityAlias.Should().Be(1);
        delta.InsertedCount.Should().Be(3);
        delta.MinDate.Should().Be(date1.Date);
        delta.MaxDate.Should().Be(date3.Date);
        delta.YearCounts.Should().ContainKey(2024).WhoseValue.Should().Be(3);
    }

    [Fact]
    public void ComputeDeltas_MultipleSecurities_SeparateDeltasWithIndependentCounts()
    {
        // Arrange
        var date1 = new DateTime(2024, 1, 15);
        var date2 = new DateTime(2024, 1, 16);

        var newPrices = new List<PriceCreateDto>
        {
            CreatePrice(1, date1),
            CreatePrice(1, date2),
            CreatePrice(2, date1)
        };

        // Act
        var deltas = SqlPriceRepository.ComputeDeltas(newPrices);

        // Assert
        deltas.Should().HaveCount(2);

        var delta1 = deltas.First(d => d.SecurityAlias == 1);
        delta1.InsertedCount.Should().Be(2);
        delta1.MinDate.Should().Be(date1.Date);
        delta1.MaxDate.Should().Be(date2.Date);
        delta1.YearCounts.Should().ContainKey(2024).WhoseValue.Should().Be(2);

        var delta2 = deltas.First(d => d.SecurityAlias == 2);
        delta2.InsertedCount.Should().Be(1);
        delta2.MinDate.Should().Be(date1.Date);
        delta2.MaxDate.Should().Be(date1.Date);
        delta2.YearCounts.Should().ContainKey(2024).WhoseValue.Should().Be(1);
    }

    [Fact]
    public void ComputeDeltas_SpanningTwoCalendarYears_CorrectYearPartition()
    {
        // Arrange
        var pricesIn2024 = new List<PriceCreateDto>
        {
            CreatePrice(1, new DateTime(2024, 12, 30)),
            CreatePrice(1, new DateTime(2024, 12, 31))
        };

        var pricesIn2025 = new List<PriceCreateDto>
        {
            CreatePrice(1, new DateTime(2025, 1, 1)),
            CreatePrice(1, new DateTime(2025, 1, 2)),
            CreatePrice(1, new DateTime(2025, 1, 3))
        };

        var newPrices = pricesIn2024.Concat(pricesIn2025).ToList();

        // Act
        var deltas = SqlPriceRepository.ComputeDeltas(newPrices);

        // Assert
        deltas.Should().HaveCount(1);
        var delta = deltas[0];
        delta.InsertedCount.Should().Be(5);
        delta.MinDate.Should().Be(new DateTime(2024, 12, 30).Date);
        delta.MaxDate.Should().Be(new DateTime(2025, 1, 3).Date);
        delta.YearCounts.Should().HaveCount(2);
        delta.YearCounts[2024].Should().Be(2);
        delta.YearCounts[2025].Should().Be(3);
    }

    [Fact]
    public void ComputeDeltas_MinDateMaxDateWithTimeComponents_StripToDateOnly()
    {
        // Arrange
        var date1WithTime = new DateTime(2024, 1, 15, 10, 30, 45);
        var date2WithTime = new DateTime(2024, 1, 17, 15, 45, 30);

        var newPrices = new List<PriceCreateDto>
        {
            CreatePrice(1, date1WithTime),
            CreatePrice(1, date2WithTime)
        };

        // Act
        var deltas = SqlPriceRepository.ComputeDeltas(newPrices);

        // Assert
        var delta = deltas[0];
        delta.MinDate.Should().Be(new DateTime(2024, 1, 15).Date);
        delta.MaxDate.Should().Be(new DateTime(2024, 1, 17).Date);
        delta.MinDate.TimeOfDay.Should().Be(TimeSpan.Zero);
        delta.MaxDate.TimeOfDay.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ComputeDeltas_UnorderedInput_ProducesCorrectDeltaWithCorrectBoundaries()
    {
        // Arrange: Deliberately provide dates out of order
        var newPrices = new List<PriceCreateDto>
        {
            CreatePrice(1, new DateTime(2024, 1, 20)),
            CreatePrice(1, new DateTime(2024, 1, 10)),
            CreatePrice(1, new DateTime(2024, 1, 30)),
            CreatePrice(1, new DateTime(2024, 1, 5))
        };

        // Act
        var deltas = SqlPriceRepository.ComputeDeltas(newPrices);

        // Assert
        var delta = deltas[0];
        delta.InsertedCount.Should().Be(4);
        delta.MinDate.Should().Be(new DateTime(2024, 1, 5).Date);
        delta.MaxDate.Should().Be(new DateTime(2024, 1, 30).Date);
    }

    [Fact]
    public void ComputeDeltas_DuplicateDatesForSecurity_AllCountedInInsertedCount()
    {
        // Arrange: Same security, same date (would not occur in practice due to DB uniqueness,
        // but test ensures counting is correct)
        var date = new DateTime(2024, 1, 15);
        var newPrices = new List<PriceCreateDto>
        {
            CreatePrice(1, date),
            CreatePrice(1, date),
            CreatePrice(1, date)
        };

        // Act
        var deltas = SqlPriceRepository.ComputeDeltas(newPrices);

        // Assert
        var delta = deltas[0];
        delta.InsertedCount.Should().Be(3);
        delta.YearCounts[2024].Should().Be(3);
    }

    [Fact]
    public void ComputeDeltas_MultipleSecuritiesWithDifferentDateRanges_IndependentBoundaries()
    {
        // Arrange
        var newPrices = new List<PriceCreateDto>
        {
            CreatePrice(1, new DateTime(2024, 1, 1)),
            CreatePrice(1, new DateTime(2024, 1, 10)),
            CreatePrice(2, new DateTime(2024, 2, 1)),
            CreatePrice(2, new DateTime(2024, 2, 28)),
            CreatePrice(3, new DateTime(2024, 6, 15))
        };

        // Act
        var deltas = SqlPriceRepository.ComputeDeltas(newPrices);

        // Assert
        deltas.Should().HaveCount(3);

        var delta1 = deltas.First(d => d.SecurityAlias == 1);
        delta1.MinDate.Should().Be(new DateTime(2024, 1, 1).Date);
        delta1.MaxDate.Should().Be(new DateTime(2024, 1, 10).Date);

        var delta2 = deltas.First(d => d.SecurityAlias == 2);
        delta2.MinDate.Should().Be(new DateTime(2024, 2, 1).Date);
        delta2.MaxDate.Should().Be(new DateTime(2024, 2, 28).Date);

        var delta3 = deltas.First(d => d.SecurityAlias == 3);
        delta3.MinDate.Should().Be(new DateTime(2024, 6, 15).Date);
        delta3.MaxDate.Should().Be(new DateTime(2024, 6, 15).Date);
    }

    [Fact]
    public void ComputeDeltas_LargeBatch_ProcessesAllSecuritiesAndDates()
    {
        // Arrange: Create 100 prices across 10 securities
        var newPrices = new List<PriceCreateDto>();
        var baseDate = new DateTime(2024, 1, 1);

        for (int securityId = 1; securityId <= 10; securityId++)
        {
            for (int dayOffset = 0; dayOffset < 10; dayOffset++)
            {
                newPrices.Add(CreatePrice(securityId, baseDate.AddDays(dayOffset)));
            }
        }

        // Act
        var deltas = SqlPriceRepository.ComputeDeltas(newPrices);

        // Assert
        deltas.Should().HaveCount(10);
        deltas.All(d => d.InsertedCount == 10).Should().BeTrue();
        deltas.All(d => d.MinDate == baseDate.Date).Should().BeTrue();
        deltas.All(d => d.MaxDate == baseDate.AddDays(9).Date).Should().BeTrue();
        deltas.All(d => d.YearCounts.ContainsKey(2024) && d.YearCounts[2024] == 10).Should().BeTrue();
    }

    /// <summary>
    /// Helper to create a PriceCreateDto with minimal data for testing.
    /// </summary>
    private static PriceCreateDto CreatePrice(int securityAlias, DateTime effectiveDate)
    {
        return new PriceCreateDto
        {
            SecurityAlias = securityAlias,
            EffectiveDate = effectiveDate,
            Open = 100m,
            High = 105m,
            Low = 99m,
            Close = 102m,
            Volatility = null,
            Volume = null,
            AdjustedClose = null
        };
    }
}
