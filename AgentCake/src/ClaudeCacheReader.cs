using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentCake;

// Claude Desktop is an Electron app, so every usage response it fetches is kept in
// Chromium's on-disk HTTP cache (Cache\Cache_Data, "blockfile" format). The Code
// tab's usage card calls api.anthropic.com/api/oauth/usage and the claude.ai UI calls
// claude.ai/api/organizations/{org}/usage; both return five_hour / seven_day
// utilization with exact reset times. This reads the newest cached copy locally;
// it never makes a network request.
public static class ClaudeCacheReader
{
    private const int FileHeaderSize = 8192;
    private const int EntrySize = 256;
    private const int KeyOffset = 96;
    private const int InlineKeyMax = EntrySize - KeyOffset;
    private const int MaxStreamBytes = 1024 * 1024;

    private static readonly Regex UsageKey = new(
        @"(api\.anthropic\.com/api/oauth/usage|claude\.ai/api/organizations/[0-9a-f-]+/usage)(\?|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public sealed record CachedUsage(ServiceUsage Usage, DateTimeOffset FetchedAt, string Url);

    public static CachedUsage? ReadNewest(IEnumerable<string> claudeDataDirs)
    {
        CachedUsage? newest = null;
        foreach (var dir in claudeDataDirs)
        {
            string cacheDir = Path.Combine(dir, "Cache", "Cache_Data");
            if (!File.Exists(Path.Combine(cacheDir, "data_1"))) continue;
            try
            {
                var found = ReadNewestFromCacheDir(cacheDir);
                if (found is not null && (newest is null || found.FetchedAt > newest.FetchedAt)) newest = found;
            }
            catch (Exception exception)
            {
                CrashLog.Write($"Claude HTTP cache could not be read: {cacheDir}", exception);
            }
        }
        return newest;
    }

    public static CachedUsage? ReadNewestFromCacheDir(string cacheDir)
    {
        using var files = new BlockFiles(cacheDir);
        byte[] entries = files.ReadAll(1);
        CachedUsage? newest = null;

        for (int offset = FileHeaderSize; offset + EntrySize <= entries.Length; offset += EntrySize)
        {
            var entry = entries.AsSpan(offset, EntrySize);
            int keyLength = BitConverter.ToInt32(entry[32..]);
            if (keyLength <= 0 || keyLength > 4096) continue;
            uint longKey = BitConverter.ToUInt32(entry[36..]);
            string key;
            if (longKey == 0)
            {
                if (keyLength > InlineKeyMax) continue;
                key = Encoding.ASCII.GetString(entry.Slice(KeyOffset, keyLength));
            }
            else
            {
                // data_1 also holds response blocks, so a "long key" here may be
                // arbitrary bytes. Skip anything that is not a readable entry.
                try { key = Encoding.ASCII.GetString(files.Read(longKey, keyLength)); }
                catch { continue; }
            }
            if (!UsageKey.IsMatch(key)) continue;

            int headerSize = BitConverter.ToInt32(entry[40..]);
            int bodySize = BitConverter.ToInt32(entry[44..]);
            uint headerAddr = BitConverter.ToUInt32(entry[56..]);
            uint bodyAddr = BitConverter.ToUInt32(entry[60..]);
            if (headerSize <= 0 || bodySize <= 0 || headerSize > MaxStreamBytes || bodySize > MaxStreamBytes) continue;

            try
            {
                string headers = Encoding.Latin1.GetString(files.Read(headerAddr, headerSize));
                byte[] body = files.Read(bodyAddr, bodySize);
                if (!TryParseCachedResponse(headers, body, out var usage, out var fetchedAt)) continue;
                string url = key[(key.IndexOf("http", StringComparison.OrdinalIgnoreCase) is var i and >= 0 ? i : 0)..];
                if (newest is null || fetchedAt > newest.FetchedAt) newest = new CachedUsage(usage, fetchedAt, url);
            }
            catch { /* stale or partially written entry */ }
        }
        return newest;
    }

    internal static bool TryParseCachedResponse(string headers, byte[] body, out ServiceUsage usage, out DateTimeOffset fetchedAt)
    {
        usage = ServiceUsage.Unavailable("Claude", "No cached Claude usage response.");
        fetchedAt = DateTimeOffset.MinValue;

        var date = Regex.Match(headers, @"(?i)\bdate:([^\x00\r\n]+?GMT)");
        if (!date.Success || !DateTimeOffset.TryParse(date.Groups[1].Value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out fetchedAt)) return false;

        string encoding = Regex.Match(headers, @"(?i)\bcontent-encoding:\s*([a-z0-9-]+)") is { Success: true } m
            ? m.Groups[1].Value.ToLowerInvariant()
            : "identity";
        byte[]? json = Decode(body, encoding);
        return json is not null && UsageParsers.TryParseClaudeUsageResponse(json, out usage);
    }

    private static byte[]? Decode(byte[] body, string encoding)
    {
        Stream? decoder = encoding switch
        {
            "identity" => null,
            "br" => new BrotliStream(new MemoryStream(body), CompressionMode.Decompress),
            "gzip" => new GZipStream(new MemoryStream(body), CompressionMode.Decompress),
            "deflate" => new ZLibStream(new MemoryStream(body), CompressionMode.Decompress),
            _ => Stream.Null // e.g. zstd: no decoder in .NET 10; the other cached copy is used instead
        };
        if (decoder is null) return body;
        if (ReferenceEquals(decoder, Stream.Null)) return null;
        using (decoder)
        {
            using var output = new MemoryStream();
            decoder.CopyTo(output);
            return output.ToArray();
        }
    }

    // Reads Chromium blockfile cache files. Chromium keeps them open while running,
    // so they are opened with full sharing and only the needed blocks are read.
    private sealed class BlockFiles : IDisposable
    {
        private readonly string _dir;
        private readonly Dictionary<string, FileStream> _open = new(StringComparer.OrdinalIgnoreCase);

        public BlockFiles(string dir) => _dir = dir;

        private FileStream Open(string name)
        {
            if (!_open.TryGetValue(name, out var stream))
            {
                stream = new FileStream(Path.Combine(_dir, name), FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                _open[name] = stream;
            }
            return stream;
        }

        public byte[] ReadAll(int fileNumber)
        {
            var stream = Open($"data_{fileNumber}");
            stream.Seek(0, SeekOrigin.Begin);
            var buffer = new byte[stream.Length];
            stream.ReadExactly(buffer);
            return buffer;
        }

        public byte[] Read(uint addr, int size)
        {
            if ((addr & 0x80000000) == 0) throw new InvalidDataException("Uninitialized cache address.");
            int fileType = (int)((addr >> 28) & 7);
            FileStream stream;
            long offset;
            if (fileType == 0)
            {
                stream = Open($"f_{addr & 0x0FFFFFFF:x6}");
                offset = 0;
            }
            else
            {
                int blockSize = fileType switch { 1 => 36, 2 => 256, 3 => 1024, 4 => 4096, _ => throw new InvalidDataException("Unknown block type.") };
                int blocks = (int)((addr >> 24) & 3) + 1;
                if (size > blocks * blockSize) throw new InvalidDataException("Stream larger than its blocks.");
                stream = Open($"data_{(addr >> 16) & 0xFF}");
                offset = FileHeaderSize + (long)(addr & 0xFFFF) * blockSize;
            }
            if (offset + size > stream.Length) throw new InvalidDataException("Stream beyond end of file.");
            var buffer = new byte[size];
            stream.Seek(offset, SeekOrigin.Begin);
            stream.ReadExactly(buffer);
            return buffer;
        }

        public void Dispose()
        {
            foreach (var stream in _open.Values) stream.Dispose();
        }
    }
}
