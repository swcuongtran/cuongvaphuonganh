using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace VietnameseSpellChecker;

public enum CandidateSource
{
    ExactOriginal,      // Từ gốc người dùng gõ
    LexiconUnaccented,  // Từ điển không dấu
    TelexDecoded,       // Giải mã từ gõ phím Telex (tooi -> tôi)
    PhoneticVariant     // Biến thể ngữ âm (s/x, tr/ch, d/gi/r, l/n)
}

public readonly record struct Candidate(string Word, CandidateSource Source);

public class NgramLanguageModel : IDisposable
{
    private readonly string _trigramsFile;
    private readonly string _bigramsFile;
    private readonly string _lexiconFile;
    private readonly string _abbreviationsFile;

    private long _fileSize;
    private FileStream? _trigramStream;
    private readonly object _fileLock = new();

    private Dictionary<string, List<(string Word, int Count)>> _lexicon = new(StringComparer.Ordinal);
    private Dictionary<string, int> _unigramCounts = new(StringComparer.Ordinal);
    private Dictionary<string, int> _bigrams = new(StringComparer.Ordinal);
    private readonly HashSet<string> _abbreviationsExact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _abbreviationsLookup = new(StringComparer.OrdinalIgnoreCase);

    // Bộ nhớ đệm LRU cho Trigram
    private readonly ConcurrentDictionary<string, long> _trigramCache = new(StringComparer.Ordinal);
    private const int MaxCacheSize = 200_000;

    public long TotalUnigrams { get; private set; } = 1;

    public NgramLanguageModel(
        string trigramsFile = "trigrams.txt",
        string bigramsFile = "bigrams.json",
        string lexiconFile = "lexicon.json",
        string abbreviationsFile = "abbreviations.txt")
    {
        _trigramsFile = trigramsFile;
        _bigramsFile = bigramsFile;
        _lexiconFile = lexiconFile;
        _abbreviationsFile = abbreviationsFile;

        InitModel();
    }

    private void InitModel()
    {
        // 1. Mở file trigrams.txt
        if (File.Exists(_trigramsFile))
        {
            _fileSize = new FileInfo(_trigramsFile).Length;
            _trigramStream = new FileStream(
                _trigramsFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 8192,
                FileOptions.RandomAccess);
        }
        else
        {
            Console.WriteLine($"[CẢNH BÁO] Không tìm thấy file '{_trigramsFile}'. Tra cứu Trigram sẽ bị vô hiệu.");
        }

        // 2. Nạp bigrams.json
        if (File.Exists(_bigramsFile))
        {
            using var fs = File.OpenRead(_bigramsFile);
            _bigrams = JsonSerializer.Deserialize<Dictionary<string, int>>(fs) ?? new(StringComparer.Ordinal);
        }
        else
        {
            Console.WriteLine($"[CẢNH BÁO] Không tìm thấy '{_bigramsFile}'.");
            _bigrams = new(StringComparer.Ordinal);
        }

        // 3. Nạp lexicon.json
        if (File.Exists(_lexiconFile))
        {
            using var fs = File.OpenRead(_lexiconFile);
            using var doc = JsonDocument.Parse(fs);

            if (doc.RootElement.TryGetProperty("total_unigrams", out var totalProp))
            {
                TotalUnigrams = totalProp.GetInt64();
            }

            if (doc.RootElement.TryGetProperty("lexicon", out var lexProp))
            {
                _lexicon = new Dictionary<string, List<(string Word, int Count)>>(205000, StringComparer.Ordinal);
                _unigramCounts = new Dictionary<string, int>(250000, StringComparer.Ordinal);

                foreach (var prop in lexProp.EnumerateObject())
                {
                    var list = new List<(string Word, int Count)>();
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        string w = item[0].GetString()!;
                        int c = item[1].GetInt32();
                        list.Add((w, c));
                        _unigramCounts.TryAdd(w, c);
                    }
                    _lexicon[prop.Name] = list;
                }
            }
        }
        else
        {
            Console.WriteLine($"[CẢNH BÁO] Không tìm thấy '{_lexiconFile}'.");
            _lexicon = new(StringComparer.Ordinal);
            _unigramCounts = new(StringComparer.Ordinal);
        }

        // 4. Nạp từ điển viết tắt abbreviations.txt nếu có
        if (File.Exists(_abbreviationsFile))
        {
            foreach (var line in File.ReadLines(_abbreviationsFile))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split('\t');
                if (parts.Length >= 2)
                {
                    string abbr = parts[1].Trim();
                    if (!string.IsNullOrEmpty(abbr))
                    {
                        _abbreviationsExact.Add(abbr);
                        _abbreviationsLookup.TryAdd(abbr, abbr);
                    }
                }
            }
        }
    }

    public long GetTrigramCount(string w1, string w2, string w3)
    {
        if (_fileSize == 0 || _trigramStream == null)
            return 0;

        string targetGram = $"{w1.ToLower()} {w2.ToLower()} {w3.ToLower()}";

        if (_trigramCache.TryGetValue(targetGram, out long cachedCount))
            return cachedCount;

        long count = SearchTrigramDisk(targetGram);

        if (_trigramCache.Count < MaxCacheSize)
        {
            _trigramCache.TryAdd(targetGram, count);
        }

        return count;
    }

    private long SearchTrigramDisk(string targetGram)
    {
        if (_trigramStream == null) return 0;

        lock (_fileLock)
        {
            long low = 0;
            long high = _fileSize;
            byte[] buffer = new byte[512];

            try
            {
                while (low < high)
                {
                    long mid = (low + high) / 2;
                    _trigramStream.Seek(mid, SeekOrigin.Begin);

                    if (mid > 0)
                    {
                        // Bỏ qua phần dòng dở dang tới dấu \n
                        int b;
                        while ((b = _trigramStream.ReadByte()) != -1)
                        {
                            if (b == '\n') break;
                        }
                    }

                    long lineStartPos = _trigramStream.Position;
                    if (lineStartPos >= _fileSize)
                    {
                        high = mid;
                        continue;
                    }

                    int bytesRead = 0;
                    int nextByte;
                    while ((nextByte = _trigramStream.ReadByte()) != -1 && nextByte != '\n' && nextByte != '\r')
                    {
                        if (bytesRead < buffer.Length)
                        {
                            buffer[bytesRead++] = (byte)nextByte;
                        }
                    }

                    if (bytesRead == 0)
                    {
                        high = mid;
                        continue;
                    }

                    string line = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    int tabIndex = line.IndexOf('\t');
                    if (tabIndex == -1)
                    {
                        high = mid;
                        continue;
                    }

                    string gram = line[..tabIndex];
                    int cmp = string.CompareOrdinal(gram, targetGram);

                    if (cmp == 0)
                    {
                        if (long.TryParse(line.AsSpan(tabIndex + 1), out long val))
                            return val;
                        return 0;
                    }
                    else if (cmp < 0)
                    {
                        if (low == lineStartPos)
                            break;
                        low = lineStartPos;
                    }
                    else
                    {
                        high = mid;
                    }
                }
            }
            catch
            {
                return 0;
            }

            return 0;
        }
    }

    public int GetBigramCount(string w1, string w2)
    {
        string key = $"{w1.ToLower()} {w2.ToLower()}";
        return _bigrams.GetValueOrDefault(key, 0);
    }

    public int GetUnigramCount(string word)
    {
        return _unigramCounts.GetValueOrDefault(word.ToLower(), 1);
    }

    public List<Candidate> GetCandidates(string word, int maxCandidates = 8)
    {
        // 1. Trường hợp CamelCase (như iPhone, ChatGPT) -> Giữ nguyên 100%
        if (TextUtils.IsCamelCase(word))
        {
            return [new Candidate(word, CandidateSource.ExactOriginal)];
        }

        // 2. Tra cứu từ viết tắt từ abbreviations.txt:
        // 2A: Người dùng gõ chính xác chữ hoa của từ viết tắt (vd: "AI", "IT", "TPHCM", "RAM", "UBND")
        if (_abbreviationsExact.Contains(word))
        {
            return [new Candidate(word, CandidateSource.ExactOriginal)];
        }

        string lowerWord = word.ToLower();
        string unaccentedKey = TextUtils.StripAccents(lowerWord);

        // 2B: Người dùng gõ chữ thường, nhưng từ này là từ viết tắt và KHÔNG PHẢI từ tiếng Việt
        // (vd: "tphcm", "w3c", "html", "php", "cpu")
        if (_abbreviationsLookup.TryGetValue(lowerWord, out string? standardAbbr))
        {
            if (!_lexicon.ContainsKey(unaccentedKey))
            {
                return [new Candidate(standardAbbr, CandidateSource.ExactOriginal)];
            }
        }

        var candidates = new List<Candidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 3. Nếu từ gốc đã có dấu, bổ sung làm ứng viên gốc
        if (TextUtils.HasAccent(lowerWord))
        {
            candidates.Add(new Candidate(lowerWord, CandidateSource.ExactOriginal));
            seen.Add(lowerWord);
        }

        // 4. Giải mã Telex nếu có (tooi -> tôi, hocj -> học, nguwoif -> người, vieetj -> việt)
        string telexDecoded = TelexDecoder.Decode(lowerWord);
        if (!string.Equals(telexDecoded, lowerWord, StringComparison.OrdinalIgnoreCase))
        {
            if (seen.Add(telexDecoded))
            {
                candidates.Add(new Candidate(telexDecoded, CandidateSource.TelexDecoded));
            }

            // Đồng thời tra cứu theo gốc không dấu của telexDecoded
            string telexUnaccented = TextUtils.StripAccents(telexDecoded);
            if (_lexicon.TryGetValue(telexUnaccented, out var telexList))
            {
                int count = 0;
                foreach (var (cand, _) in telexList)
                {
                    if (seen.Add(cand))
                    {
                        var src = string.Equals(cand, telexDecoded, StringComparison.OrdinalIgnoreCase)
                            ? CandidateSource.TelexDecoded
                            : CandidateSource.LexiconUnaccented;
                        candidates.Add(new Candidate(cand, src));
                        count++;
                        if (count >= 4) break;
                    }
                }
            }
        }

        // 5. Tra cứu Lexicon chuẩn từ dạng không dấu của từ gốc
        if (_lexicon.TryGetValue(unaccentedKey, out var candList))
        {
            int count = 0;
            foreach (var (cand, _) in candList)
            {
                if (seen.Add(cand))
                {
                    candidates.Add(new Candidate(cand, CandidateSource.LexiconUnaccented));
                    count++;
                    if (count >= maxCandidates) break;
                }
            }
        }

        // 6. Mở rộng ứng viên theo ngữ âm tương đồng (s/x, tr/ch, d/gi/r, l/n)
        var phoneticVariants = PhoneticUtils.GetPhoneticVariants(unaccentedKey);
        foreach (var variant in phoneticVariants)
        {
            if (_lexicon.TryGetValue(variant, out var varList))
            {
                int added = 0;
                foreach (var (cand, _) in varList)
                {
                    if (seen.Add(cand))
                    {
                        candidates.Add(new Candidate(cand, CandidateSource.PhoneticVariant));
                        added++;
                        if (added >= 3) break; // Lấy top 3 ứng viên ngữ âm
                    }
                }
            }
        }

        // 7. Luôn đảm bảo từ gốc có mặt trong danh sách ứng viên
        if (seen.Add(lowerWord))
        {
            candidates.Add(new Candidate(lowerWord, CandidateSource.ExactOriginal));
        }

        return candidates;
    }

    public double ScoreNgram(string? prev2, string? prev1, Candidate cand, string originalWord)
    {
        string current = cand.Word;
        int unigramCount = GetUnigramCount(current);
        double score = Math.Log(unigramCount + 1);

        // 1. Bigram bonus
        if (prev1 != null)
        {
            int bigramCount = GetBigramCount(prev1, current);
            if (bigramCount > 0)
            {
                score += Math.Log(bigramCount + 1) * 2.2;
            }
        }

        // 2. Trigram bonus
        if (prev2 != null && prev1 != null)
        {
            long trigramCount = GetTrigramCount(prev2, prev1, current);
            if (trigramCount > 0)
            {
                score += Math.Log(trigramCount + 1) * 3.2;
            }
        }

        // 3. Original Accent Bonus (+1.5 điểm)
        // Nếu người dùng đã gõ từ có dấu đúng, ưu tiên giữ nguyên
        if (cand.Source == CandidateSource.ExactOriginal && TextUtils.HasAccent(originalWord))
        {
            score += 1.5;
        }

        // 4. Telex Decoded Bonus (+1.2 điểm)
        if (cand.Source == CandidateSource.TelexDecoded)
        {
            score += 1.2;
        }

        // 5. Phonetic Penalty (-1.0 điểm)
        // Trừ nhẹ điểm các biến thể s/x, tr/ch để tránh đổi nhầm khi không có ngữ cảnh rõ ràng
        if (cand.Source == CandidateSource.PhoneticVariant)
        {
            score -= 1.0;
        }

        return score;
    }

    public void Dispose()
    {
        _trigramStream?.Dispose();
    }
}
