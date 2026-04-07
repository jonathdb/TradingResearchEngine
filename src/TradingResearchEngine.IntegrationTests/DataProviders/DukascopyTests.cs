using System.Buffers.Binary;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TradingResearchEngine.IntegrationTests.DataProviders;

/// <summary>
/// Step-by-step integration tests for Dukascopy data feed.
/// Each test isolates one part of the pipeline to identify failures.
/// </summary>
public class DukascopyTests
{
    private readonly HttpClient _http = new();
    private readonly ILogger _logger = NullLoggerFactory.Instance.CreateLogger("DukascopyTests");

    private const string BaseUrl = "https://datafeed.dukascopy.com/datafeed";

    [Fact]
    public async Task Step1_CanDownloadBi5File()
    {
        // EURUSD, Jan 2 2024, 1-minute bid candles
        var url = $"{BaseUrl}/EURUSD/2024/00/02/BID_candles_min_1.bi5";
        var response = await _http.GetAsync(url);

        Assert.True(response.IsSuccessStatusCode,
            $"Expected 200, got {(int)response.StatusCode}");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0, "Downloaded file is empty");

        // Log the file details for debugging
        _logger.LogInformation("Downloaded {Bytes} bytes from {Url}", bytes.Length, url);
        _logger.LogInformation("First 20 bytes: {Hex}",
            string.Join(" ", bytes.Take(20).Select(b => b.ToString("X2"))));
    }

    [Fact]
    public async Task Step2_AnalyzeBi5Header()
    {
        var url = $"{BaseUrl}/EURUSD/2024/00/02/BID_candles_min_1.bi5";
        var bytes = await _http.GetByteArrayAsync(url);

        Assert.True(bytes.Length >= 13, $"File too small: {bytes.Length} bytes");

        // LZMA header: 5 bytes properties + 8 bytes uncompressed size
        byte[] props = bytes[..5];
        long uncompressedSize = BitConverter.ToInt64(bytes, 5);

        _logger.LogInformation("LZMA properties: {Props}",
            string.Join(" ", props.Select(b => b.ToString("X2"))));
        _logger.LogInformation("Uncompressed size: {Size} bytes", uncompressedSize);
        _logger.LogInformation("Compressed data starts at offset 13, remaining: {Remaining} bytes",
            bytes.Length - 13);

        // Sanity checks
        Assert.True(uncompressedSize > 0, $"Uncompressed size is {uncompressedSize}");
        Assert.True(uncompressedSize < 100_000_000, $"Uncompressed size too large: {uncompressedSize}");

        // Each candle is 24 bytes, so uncompressed size should be divisible by 24
        long expectedCandles = uncompressedSize / 24;
        _logger.LogInformation("Expected candles: {Count} (remainder: {Rem})",
            expectedCandles, uncompressedSize % 24);
    }

    [Fact]
    public async Task Step3_DecompressWithDotNetLzma()
    {
        var url = $"{BaseUrl}/EURUSD/2024/00/02/BID_candles_min_1.bi5";
        var compressed = await _http.GetByteArrayAsync(url);

        Assert.True(compressed.Length >= 13);

        byte[] props = compressed[..5];
        long uncompressedSize = BitConverter.ToInt64(compressed, 5);

        // Try decompression using System.IO.Compression (if available) or manual LZMA
        byte[] decompressed;
        try
        {
            decompressed = DecompressLzmaManual(compressed);
            _logger.LogInformation("Decompressed: {Input} bytes -> {Output} bytes",
                compressed.Length, decompressed.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Decompression failed");
            Assert.Fail($"Decompression threw: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        Assert.True(decompressed.Length > 0, "Decompressed data is empty");
        Assert.Equal(uncompressedSize, decompressed.Length);
    }

    [Fact]
    public async Task Step4_ParseDecompressedCandles()
    {
        var url = $"{BaseUrl}/EURUSD/2024/00/02/BID_candles_min_1.bi5";
        var compressed = await _http.GetByteArrayAsync(url);
        var decompressed = DecompressLzmaManual(compressed);

        const int RecordSize = 24;
        int candleCount = decompressed.Length / RecordSize;
        _logger.LogInformation("Parsing {Count} candles from {Bytes} bytes", candleCount, decompressed.Length);

        Assert.True(candleCount > 0, "No candles parsed");

        // Parse first candle
        var record = new byte[RecordSize];
        Array.Copy(decompressed, 0, record, 0, RecordSize);
        int timeOffsetMs = BinaryPrimitives.ReadInt32BigEndian(record.AsSpan(0, 4));
        int openPip = BinaryPrimitives.ReadInt32BigEndian(record.AsSpan(4, 4));
        int highPip = BinaryPrimitives.ReadInt32BigEndian(record.AsSpan(8, 4));
        int lowPip = BinaryPrimitives.ReadInt32BigEndian(record.AsSpan(12, 4));
        int closePip = BinaryPrimitives.ReadInt32BigEndian(record.AsSpan(16, 4));
        int volBits = BinaryPrimitives.ReadInt32BigEndian(record.AsSpan(20, 4));
        float volume = BitConverter.Int32BitsToSingle(volBits);

        decimal pointSize = 100_000m; // EURUSD
        decimal open = openPip / pointSize;
        decimal high = highPip / pointSize;
        decimal low = lowPip / pointSize;
        decimal close = closePip / pointSize;

        _logger.LogInformation(
            "First candle: offset={Offset}ms O={Open} H={High} L={Low} C={Close} V={Vol}",
            timeOffsetMs, open, high, low, close, volume);

        // EURUSD should be around 1.0-1.2 range
        Assert.True(open > 0.5m && open < 2.0m,
            $"Open price {open} looks wrong for EURUSD");
        Assert.True(high >= open || high >= close,
            $"High {high} should be >= open {open} or close {close}");
    }

    [Fact]
    public async Task Step5_MultipleSymbolsWork()
    {
        var symbols = new[] { "EURUSD", "GBPUSD", "XAUUSD" };
        foreach (var symbol in symbols)
        {
            var url = $"{BaseUrl}/{symbol}/2024/00/02/BID_candles_min_1.bi5";
            var response = await _http.GetAsync(url);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            _logger.LogInformation("{Symbol}: HTTP {Status}, {Bytes} bytes",
                symbol, (int)response.StatusCode, bytes.Length);

            Assert.True(response.IsSuccessStatusCode,
                $"{symbol} returned {(int)response.StatusCode}");
            Assert.True(bytes.Length > 0, $"{symbol} returned empty data");
        }
    }

    /// <summary>
    /// Manual LZMA decompression for Dukascopy bi5 files.
    /// bi5 format: 5 bytes LZMA props + 8 bytes uncompressed size (LE int64) + compressed data.
    /// This uses the LZMA SDK decoder embedded in the test.
    /// </summary>
    private static byte[] DecompressLzmaManual(byte[] data)
    {
        if (data.Length < 13)
            throw new InvalidOperationException($"Data too short for LZMA header: {data.Length} bytes");

        byte[] properties = data[..5];
        long uncompressedSize = BitConverter.ToInt64(data, 5);

        if (uncompressedSize <= 0 || uncompressedSize > 50_000_000)
            throw new InvalidOperationException($"Invalid uncompressed size: {uncompressedSize}");

        // Use SharpCompress LzmaStream
        using var inputStream = new MemoryStream(data, 13, data.Length - 13);
        using var outputStream = new MemoryStream((int)uncompressedSize);

        // SharpCompress LzmaStream constructor: (properties, inputStream, compressedSize, uncompressedSize)
        using var lzma = new SharpCompress.Compressors.LZMA.LzmaStream(
            properties, inputStream, data.Length - 13, uncompressedSize);

        // Read in chunks with a timeout to detect hangs
        byte[] buffer = new byte[8192];
        int totalRead = 0;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (totalRead < uncompressedSize)
        {
            if (cts.Token.IsCancellationRequested)
                throw new TimeoutException($"LZMA decompression timed out after reading {totalRead} of {uncompressedSize} bytes");

            int read = lzma.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            outputStream.Write(buffer, 0, read);
            totalRead += read;
        }

        return outputStream.ToArray();
    }
}
