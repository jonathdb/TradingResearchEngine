using System.Buffers.Binary;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpCompress.Compressors.LZMA;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Infrastructure.DataProviders;

namespace TradingResearchEngine.IntegrationTests.DataProviders;

/// <summary>
/// Integration tests for the DukascopyDataProvider tick caching pipeline.
/// Uses a mock HTTP handler to avoid real network calls while testing the full
/// download → decompress → parse → cache → load flow.
/// </summary>
public class DukascopyTickCacheIntegrationTests : IDisposable
{
    private readonly string _tempCacheDir;

    public DukascopyTickCacheIntegrationTests()
    {
        _tempCacheDir = Path.Combine(Path.GetTempPath(), $"dukascopy-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempCacheDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempCacheDir))
            Directory.Delete(_tempCacheDir, recursive: true);
    }

    [Fact]
    public async Task GetTicks_FirstDownload_CreatesCacheFilePerTradingDay()
    {
        // Arrange: Monday 2023-01-02 and Tuesday 2023-01-03
        var from = new DateTimeOffset(2023, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2023, 1, 3, 23, 59, 59, TimeSpan.Zero);

        const int ticksPerHour = 5;
        var mockHandler = new MockDukascopyHttpHandler("EURUSD", ticksPerHour);
        var httpClient = new HttpClient(mockHandler);
        var logger = NullLoggerFactory.Instance.CreateLogger<DukascopyDataProvider>();

        var provider = new DukascopyDataProvider(httpClient, logger, cacheDir: _tempCacheDir);

        // Act: consume all ticks
        var allTicks = new List<TradingResearchEngine.Core.DataHandling.TickRecord>();
        await foreach (var tick in provider.GetTicks("EURUSD", from, to))
        {
            allTicks.Add(tick);
        }

        // Assert: ticks were returned
        Assert.True(allTicks.Count > 0, "Expected ticks to be returned from mock");

        // Assert: one CSV cache file per trading day
        var day1CachePath = DukascopyHelpers.GetTickCachePath(_tempCacheDir, "EURUSD", new DateTime(2023, 1, 2));
        var day2CachePath = DukascopyHelpers.GetTickCachePath(_tempCacheDir, "EURUSD", new DateTime(2023, 1, 3));

        Assert.True(File.Exists(day1CachePath), $"Cache file for 2023-01-02 should exist at {day1CachePath}");
        Assert.True(File.Exists(day2CachePath), $"Cache file for 2023-01-03 should exist at {day2CachePath}");

        // Assert: cache files pass validity check
        Assert.True(DukascopyHelpers.IsCacheFileValid(day1CachePath),
            "Cache file for 2023-01-02 should pass IsCacheFileValid");
        Assert.True(DukascopyHelpers.IsCacheFileValid(day2CachePath),
            "Cache file for 2023-01-03 should pass IsCacheFileValid");

        // Assert: cache files are loadable and contain expected tick count
        var loadedDay1 = DukascopyHelpers.LoadTicksFromCsv(day1CachePath, "EURUSD");
        var loadedDay2 = DukascopyHelpers.LoadTicksFromCsv(day2CachePath, "EURUSD");

        // Mock returns ticksPerHour ticks for each of 24 hours = 24 * ticksPerHour per day
        int expectedPerDay = 24 * ticksPerHour;
        Assert.Equal(expectedPerDay, loadedDay1.Count);
        Assert.Equal(expectedPerDay, loadedDay2.Count);

        // Assert: loaded ticks have valid data
        foreach (var tick in loadedDay1)
        {
            Assert.Equal("EURUSD", tick.Symbol);
            Assert.True(tick.BidLevels[0].Price > 0, "Bid price should be positive");
            Assert.True(tick.AskLevels[0].Price > 0, "Ask price should be positive");
            Assert.True(tick.AskLevels[0].Price >= tick.BidLevels[0].Price, "Ask should be >= Bid");
        }

        // Assert: cache path matches expected convention
        var expectedPath = Path.Combine(_tempCacheDir, "EURUSD", "ticks", "2023", "01", "02.csv");
        Assert.Equal(Path.GetFullPath(expectedPath), Path.GetFullPath(day1CachePath));
    }

    [Fact]
    public async Task GetTicks_FirstDownload_TicksAreChronologicallyOrdered()
    {
        // Arrange: single day
        var from = new DateTimeOffset(2023, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2023, 1, 2, 23, 59, 59, TimeSpan.Zero);

        var mockHandler = new MockDukascopyHttpHandler("EURUSD", ticksPerHour: 3);
        var httpClient = new HttpClient(mockHandler);
        var logger = NullLoggerFactory.Instance.CreateLogger<DukascopyDataProvider>();

        var provider = new DukascopyDataProvider(httpClient, logger, cacheDir: _tempCacheDir);

        // Act
        var allTicks = new List<TradingResearchEngine.Core.DataHandling.TickRecord>();
        await foreach (var tick in provider.GetTicks("EURUSD", from, to))
        {
            allTicks.Add(tick);
        }

        // Assert: ticks are in non-decreasing timestamp order
        for (int i = 1; i < allTicks.Count; i++)
        {
            Assert.True(allTicks[i].Timestamp >= allTicks[i - 1].Timestamp,
                $"Tick at index {i} has timestamp {allTicks[i].Timestamp} which is before {allTicks[i - 1].Timestamp}");
        }
    }

    [Fact]
    public async Task GetTicks_CacheHit_SkipsNetworkOnSecondCall()
    {
        // Arrange: pre-populate cache with valid tick data for 2023-01-02 (Monday)
        var date = new DateTime(2023, 1, 2);
        var from = new DateTimeOffset(2023, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2023, 1, 2, 23, 59, 59, TimeSpan.Zero);

        // Create 10 known ticks
        var expectedTicks = new List<TickRecord>();
        for (int i = 0; i < 10; i++)
        {
            var timestamp = from.AddMinutes(i * 10);
            expectedTicks.Add(new TickRecord(
                "EURUSD",
                new[] { new BidLevel(1.06845m, 1.5m) },
                new[] { new AskLevel(1.06847m, 2.0m) },
                new LastTrade(1.06846m, 1.5m, timestamp),
                timestamp));
        }

        // Write ticks to the correct cache path
        var cachePath = DukascopyHelpers.GetTickCachePath(_tempCacheDir, "EURUSD", date);
        DukascopyHelpers.SaveTicksToCsv(cachePath, expectedTicks);

        // Create a provider with a throwing HTTP handler — any network call will fail the test
        var throwingHandler = new ThrowingHttpHandler();
        var httpClient = new HttpClient(throwingHandler);
        var logger = NullLoggerFactory.Instance.CreateLogger<DukascopyDataProvider>();

        var provider = new DukascopyDataProvider(httpClient, logger, cacheDir: _tempCacheDir);

        // Act: call GetTicks for the cached date range
        var returnedTicks = new List<TickRecord>();
        await foreach (var tick in provider.GetTicks("EURUSD", from, to))
        {
            returnedTicks.Add(tick);
        }

        // Assert: ticks returned successfully (no exception from throwing handler)
        Assert.Equal(expectedTicks.Count, returnedTicks.Count);

        // Assert: tick data matches what was written to cache
        for (int i = 0; i < expectedTicks.Count; i++)
        {
            Assert.Equal(expectedTicks[i].Symbol, returnedTicks[i].Symbol);
            Assert.Equal(expectedTicks[i].BidLevels[0].Price, returnedTicks[i].BidLevels[0].Price);
            Assert.Equal(expectedTicks[i].BidLevels[0].Size, returnedTicks[i].BidLevels[0].Size);
            Assert.Equal(expectedTicks[i].AskLevels[0].Price, returnedTicks[i].AskLevels[0].Price);
            Assert.Equal(expectedTicks[i].AskLevels[0].Size, returnedTicks[i].AskLevels[0].Size);
            Assert.Equal(expectedTicks[i].LastTrade.Price, returnedTicks[i].LastTrade.Price);
            Assert.Equal(expectedTicks[i].LastTrade.Volume, returnedTicks[i].LastTrade.Volume);
            Assert.Equal(expectedTicks[i].Timestamp, returnedTicks[i].Timestamp);
        }
    }

    [Fact]
    public async Task GetTicks_CorruptedCache_TriggersReDownload()
    {
        // Arrange: write a truncated/invalid file (≤ 60 bytes) to the cache path for 2023-01-02
        var date = new DateTime(2023, 1, 2);
        var from = new DateTimeOffset(2023, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2023, 1, 2, 23, 59, 59, TimeSpan.Zero);

        var cachePath = DukascopyHelpers.GetTickCachePath(_tempCacheDir, "EURUSD", date);
        File.WriteAllText(cachePath, "corrupted");

        // Verify the file is invalid (≤ 60 bytes)
        Assert.True(new FileInfo(cachePath).Length <= 60, "Corrupted file should be ≤ 60 bytes");
        Assert.False(DukascopyHelpers.IsCacheFileValid(cachePath), "Corrupted file should fail validity check");

        // Create provider with a working mock HTTP handler
        const int ticksPerHour = 5;
        var mockHandler = new MockDukascopyHttpHandler("EURUSD", ticksPerHour);
        var httpClient = new HttpClient(mockHandler);
        var logger = NullLoggerFactory.Instance.CreateLogger<DukascopyDataProvider>();

        var provider = new DukascopyDataProvider(httpClient, logger, cacheDir: _tempCacheDir);

        // Act: call GetTicks and consume all ticks
        var allTicks = new List<TickRecord>();
        await foreach (var tick in provider.GetTicks("EURUSD", from, to))
        {
            allTicks.Add(tick);
        }

        // Assert: ticks were returned (the mock data was downloaded)
        int expectedPerDay = 24 * ticksPerHour;
        Assert.Equal(expectedPerDay, allTicks.Count);

        // Assert: the cache file now passes IsCacheFileValid (it was overwritten with valid data)
        Assert.True(DukascopyHelpers.IsCacheFileValid(cachePath),
            "Cache file should pass IsCacheFileValid after re-download");

        // Assert: the cache file is loadable via LoadTicksFromCsv and contains the expected tick count
        var loadedTicks = DukascopyHelpers.LoadTicksFromCsv(cachePath, "EURUSD");
        Assert.Equal(expectedPerDay, loadedTicks.Count);

        // Assert: the file size is now > 60 bytes
        Assert.True(new FileInfo(cachePath).Length > 60,
            "Cache file should be > 60 bytes after re-download");
    }

    [Fact]
    public async Task GetTicks_PartialHourFailure_StillYieldsRemainingTicks()
    {
        // Arrange: Monday 2023-01-02, with hours 3, 7, 15 failing
        var from = new DateTimeOffset(2023, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2023, 1, 2, 23, 59, 59, TimeSpan.Zero);

        const int ticksPerHour = 5;
        var failingHours = new HashSet<int> { 3, 7, 15 };

        var mockHandler = new PartialFailureHttpHandler("EURUSD", ticksPerHour, failingHours);
        var httpClient = new HttpClient(mockHandler);
        var logger = NullLoggerFactory.Instance.CreateLogger<DukascopyDataProvider>();

        var provider = new DukascopyDataProvider(httpClient, logger, cacheDir: _tempCacheDir);

        // Act: consume all ticks — should not throw
        var allTicks = new List<TickRecord>();
        await foreach (var tick in provider.GetTicks("EURUSD", from, to))
        {
            allTicks.Add(tick);
        }

        // Assert: ticks are returned from the successful hours only
        int expectedTickCount = (24 - failingHours.Count) * ticksPerHour;
        Assert.Equal(expectedTickCount, allTicks.Count);

        // Assert: cache file is created (since some hours succeeded)
        var cachePath = DukascopyHelpers.GetTickCachePath(_tempCacheDir, "EURUSD", new DateTime(2023, 1, 2));
        Assert.True(File.Exists(cachePath), $"Cache file should exist at {cachePath}");
        Assert.True(DukascopyHelpers.IsCacheFileValid(cachePath),
            "Cache file should pass IsCacheFileValid");

        // Assert: cache file contains ticks only from successful hours
        var loadedTicks = DukascopyHelpers.LoadTicksFromCsv(cachePath, "EURUSD");
        Assert.Equal(expectedTickCount, loadedTicks.Count);

        // Assert: no ticks from failing hours are present
        // Failing hours: 3, 7, 15 — ticks from those hours would have timestamps in those hour ranges
        foreach (var tick in loadedTicks)
        {
            int tickHour = tick.Timestamp.UtcDateTime.Hour;
            Assert.DoesNotContain(tickHour, failingHours);
        }
    }

    /// <summary>
    /// HTTP handler that throws HttpRequestException on any request.
    /// Used to prove no network calls are made when cache is populated.
    /// </summary>
    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException(
                $"Network call should not have been made. URL: {request.RequestUri}");
        }
    }

    /// <summary>
    /// Mock HTTP handler that returns valid LZMA-compressed tick data for Dukascopy tick URLs.
    /// Returns 404 for any non-matching URLs.
    /// </summary>
    private sealed class MockDukascopyHttpHandler : HttpMessageHandler
    {
        private readonly string _symbol;
        private readonly int _ticksPerHour;

        public MockDukascopyHttpHandler(string symbol, int ticksPerHour)
        {
            _symbol = symbol;
            _ticksPerHour = ticksPerHour;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";

            // Match tick URLs: .../EURUSD/2023/00/02/05h_ticks.bi5
            if (url.Contains($"/{_symbol}/") && url.EndsWith("h_ticks.bi5"))
            {
                // Extract hour from URL to generate appropriate timestamps
                var fileName = Path.GetFileName(url); // e.g. "05h_ticks.bi5"
                var hourStr = fileName[..2];
                if (int.TryParse(hourStr, out _))
                {
                    var compressed = BuildCompressedTickData(_ticksPerHour);
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(compressed)
                    };
                    return Task.FromResult(response);
                }
            }

            // Return 404 for unrecognized URLs
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }

        /// <summary>
        /// Builds valid LZMA-compressed tick data in Dukascopy .bi5 format.
        /// Format: [5 bytes LZMA props][8 bytes uncompressed size LE int64][compressed data]
        /// Each tick is 20 bytes: uint32 ms offset (BE), uint32 ask (BE), uint32 bid (BE),
        /// int32 ask volume as float bits (BE), int32 bid volume as float bits (BE).
        /// </summary>
        private static byte[] BuildCompressedTickData(int tickCount)
        {
            // Build raw tick binary data (20 bytes per tick)
            var raw = new byte[tickCount * 20];
            const decimal pointSize = 100_000m; // EURUSD

            for (int i = 0; i < tickCount; i++)
            {
                int offset = i * 20;
                uint ms = (uint)(i * 1000); // 1 second apart within the hour
                uint ask = (uint)(1.06850m * pointSize); // 106850
                uint bid = (uint)(1.06845m * pointSize); // 106845
                float askVol = 1.5f;
                float bidVol = 2.0f;

                BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(offset, 4), ms);
                BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(offset + 4, 4), ask);
                BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(offset + 8, 4), bid);
                BinaryPrimitives.WriteInt32BigEndian(raw.AsSpan(offset + 12, 4), BitConverter.SingleToInt32Bits(askVol));
                BinaryPrimitives.WriteInt32BigEndian(raw.AsSpan(offset + 16, 4), BitConverter.SingleToInt32Bits(bidVol));
            }

            // LZMA compress using SharpCompress
            return LzmaCompress(raw);
        }

        /// <summary>
        /// Compresses raw data using LZMA in the Dukascopy .bi5 format:
        /// [5 bytes LZMA properties][8 bytes uncompressed size LE int64][compressed data]
        /// </summary>
        private static byte[] LzmaCompress(byte[] raw)
        {
            using var compressedStream = new MemoryStream();

            // Create LZMA encoder stream — writes compressed data to compressedStream
            var encoderProps = new LzmaEncoderProperties(false, 1 << 16, 32);
            using (var lzmaStream = new LzmaStream(encoderProps, false, compressedStream))
            {
                lzmaStream.Write(raw, 0, raw.Length);
            }

            var compressedData = compressedStream.ToArray();

            // Get the 5-byte LZMA properties header
            // Default SharpCompress properties: lc=3, lp=0, pb=2, dict=65536
            // Property byte = lc + lp*9 + pb*9*5 = 3 + 0 + 90 = 93 = 0x5D
            var props = new byte[5];
            props[0] = (byte)(3 + 0 * 9 + 2 * 9 * 5); // 0x5D
            int dictionary = 1 << 16; // 65536
            props[1] = (byte)(dictionary >> 0);
            props[2] = (byte)(dictionary >> 8);
            props[3] = (byte)(dictionary >> 16);
            props[4] = (byte)(dictionary >> 24);

            // Build the final .bi5 format: props + uncompressed size + compressed data
            using var output = new MemoryStream();
            output.Write(props, 0, 5);
            output.Write(BitConverter.GetBytes((long)raw.Length)); // 8 bytes LE
            output.Write(compressedData);

            return output.ToArray();
        }
    }

    /// <summary>
    /// Mock HTTP handler that returns valid LZMA-compressed tick data for most hours
    /// but returns HTTP 500 for specific failing hours. Used to test partial hour failure resilience.
    /// </summary>
    private sealed class PartialFailureHttpHandler : HttpMessageHandler
    {
        private readonly string _symbol;
        private readonly int _ticksPerHour;
        private readonly HashSet<int> _failingHours;

        public PartialFailureHttpHandler(string symbol, int ticksPerHour, HashSet<int> failingHours)
        {
            _symbol = symbol;
            _ticksPerHour = ticksPerHour;
            _failingHours = failingHours;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";

            // Match tick URLs: .../EURUSD/2023/00/02/05h_ticks.bi5
            if (url.Contains($"/{_symbol}/") && url.EndsWith("h_ticks.bi5"))
            {
                var fileName = Path.GetFileName(url); // e.g. "05h_ticks.bi5"
                var hourStr = fileName[..2];
                if (int.TryParse(hourStr, out var hour))
                {
                    // Return 500 for failing hours — triggers HttpRequestException after retries
                    if (_failingHours.Contains(hour))
                    {
                        return Task.FromResult(new HttpResponseMessage(
                            System.Net.HttpStatusCode.InternalServerError));
                    }

                    var compressed = BuildCompressedTickData(_ticksPerHour);
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(compressed)
                    };
                    return Task.FromResult(response);
                }
            }

            // Return 404 for unrecognized URLs
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }

        private static byte[] BuildCompressedTickData(int tickCount)
        {
            var raw = new byte[tickCount * 20];
            const decimal pointSize = 100_000m;

            for (int i = 0; i < tickCount; i++)
            {
                int offset = i * 20;
                uint ms = (uint)(i * 1000);
                uint ask = (uint)(1.06850m * pointSize);
                uint bid = (uint)(1.06845m * pointSize);
                float askVol = 1.5f;
                float bidVol = 2.0f;

                BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(offset, 4), ms);
                BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(offset + 4, 4), ask);
                BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(offset + 8, 4), bid);
                BinaryPrimitives.WriteInt32BigEndian(raw.AsSpan(offset + 12, 4), BitConverter.SingleToInt32Bits(askVol));
                BinaryPrimitives.WriteInt32BigEndian(raw.AsSpan(offset + 16, 4), BitConverter.SingleToInt32Bits(bidVol));
            }

            return LzmaCompress(raw);
        }

        private static byte[] LzmaCompress(byte[] raw)
        {
            using var compressedStream = new MemoryStream();

            var encoderProps = new LzmaEncoderProperties(false, 1 << 16, 32);
            using (var lzmaStream = new LzmaStream(encoderProps, false, compressedStream))
            {
                lzmaStream.Write(raw, 0, raw.Length);
            }

            var compressedData = compressedStream.ToArray();

            var props = new byte[5];
            props[0] = (byte)(3 + 0 * 9 + 2 * 9 * 5); // 0x5D
            int dictionary = 1 << 16;
            props[1] = (byte)(dictionary >> 0);
            props[2] = (byte)(dictionary >> 8);
            props[3] = (byte)(dictionary >> 16);
            props[4] = (byte)(dictionary >> 24);

            using var output = new MemoryStream();
            output.Write(props, 0, 5);
            output.Write(BitConverter.GetBytes((long)raw.Length));
            output.Write(compressedData);

            return output.ToArray();
        }
    }
}
