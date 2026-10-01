using System;
using System.Collections.Generic;
using System.Text;

namespace VietnameseSpellChecker;

public static class TelexDecoder
{
    private static readonly Dictionary<char, char[]> ToneMap = new()
    {
        ['a'] = ['á', 'à', 'ả', 'ã', 'ạ'],
        ['â'] = ['ấ', 'ầ', 'ẩ', 'ẫ', 'ậ'],
        ['ă'] = ['ắ', 'ằ', 'ẳ', 'ẵ', 'ặ'],
        ['e'] = ['é', 'è', 'ẻ', 'ẽ', 'ẹ'],
        ['ê'] = ['ế', 'ề', 'ể', 'ễ', 'ệ'],
        ['i'] = ['í', 'ì', 'ỉ', 'ĩ', 'ị'],
        ['o'] = ['ó', 'ò', 'ỏ', 'õ', 'ọ'],
        ['ô'] = ['ố', 'ồ', 'ổ', 'ỗ', 'ộ'],
        ['ơ'] = ['ớ', 'ờ', 'ở', 'ỡ', 'ợ'],
        ['u'] = ['ú', 'ù', 'ủ', 'ũ', 'ụ'],
        ['ư'] = ['ứ', 'ừ', 'ử', 'ữ', 'ự'],
        ['y'] = ['ý', 'ỳ', 'ỷ', 'ỹ', 'ỵ']
    };

    private static readonly Dictionary<char, int> ToneCharToIndex = new()
    {
        ['s'] = 0, // Sắc
        ['f'] = 1, // Huyền
        ['r'] = 2, // Hỏi
        ['x'] = 3, // Ngã
        ['j'] = 4  // Nặng
    };

    public static string Decode(string word)
    {
        if (string.IsNullOrEmpty(word) || word.Length < 2)
            return word;

        string w = word.ToLower();

        // 1. Phụ âm 'dd' -> 'đ'
        w = w.Replace("dd", "đ"); 

        // 2. Tìm và bóc tách dấu thanh Telex nếu có
        int toneIndex = -1;
        for (int i = w.Length - 1; i >= 1; i--)
        {
            char c = w[i];
            if (ToneCharToIndex.TryGetValue(c, out int tIdx))
            {
                // Chỉ bóc nếu đứng sau nguyên âm / bán nguyên âm
                toneIndex = tIdx;
                w = w.Remove(i, 1);
                break;
            }
        }

        // 3. Biến đổi nguyên âm Telex
        // Cụm ươ: uwo, uow, uwow, uơ, ưo
        w = w.Replace("uwow", "ươ");
        w = w.Replace("uwo", "ươ");
        w = w.Replace("uow", "ươ");
        w = w.Replace("ưo", "ươ");
        w = w.Replace("uơ", "ươ");

        w = w.Replace("uw", "ư");
        w = w.Replace("ow", "ơ");
        w = w.Replace("aa", "â");
        w = w.Replace("aw", "ă");
        w = w.Replace("ee", "ê");
        w = w.Replace("oo", "ô");

        // Xử lý 'w' đứng đơn lẻ sau phụ âm (như 'gw', 'ngw', 'nhw', 'w')
        w = w.Replace("w", "ư");

        // 4. Nếu có dấu thanh, gán dấu vào đúng nguyên âm chuẩn tiếng Việt
        if (toneIndex >= 0)
        {
            w = ApplyTone(w, toneIndex);
        }

        return w;
    }

    private static string ApplyTone(string word, int toneIndex)
    {
        var vowelPositions = new List<int>();
        for (int i = 0; i < word.Length; i++)
        {
            if (ToneMap.ContainsKey(word[i]))
            {
                vowelPositions.Add(i);
            }
        }

        if (vowelPositions.Count == 0) return word;

        int targetPos;
        if (vowelPositions.Count == 1)
        {
            targetPos = vowelPositions[0];
        }
        else
        {
            // Kiểm tra các trường hợp đặc biệt về cụm nguyên âm:
            // "ươ", "ươi", "ươu" -> dấu trên 'ơ'
            int uoIdx = word.IndexOf("ươ", StringComparison.Ordinal);
            if (uoIdx != -1)
            {
                targetPos = uoIdx + 1; // 'ơ'
            }
            // "iê", "yê" -> dấu trên 'ê'
            else if (word.Contains("iê", StringComparison.Ordinal))
            {
                targetPos = word.IndexOf("iê", StringComparison.Ordinal) + 1;
            }
            else if (word.Contains("yê", StringComparison.Ordinal))
            {
                targetPos = word.IndexOf("yê", StringComparison.Ordinal) + 1;
            }
            // "uô" -> dấu trên 'ô'
            else if (word.Contains("uô", StringComparison.Ordinal))
            {
                targetPos = word.IndexOf("uô", StringComparison.Ordinal) + 1;
            }
            else
            {
                bool hasEndingConsonant = !ToneMap.ContainsKey(word[^1]);
                if (hasEndingConsonant)
                {
                    targetPos = vowelPositions.Count >= 2 ? vowelPositions[1] : vowelPositions[0];
                }
                else
                {
                    string sub = word.Substring(vowelPositions[0], vowelPositions.Count);
                    if (sub is "ia" or "ua" or "ưa")
                        targetPos = vowelPositions[0];
                    else
                        targetPos = vowelPositions[^1];
                }
            }
        }

        char targetVowel = word[targetPos];
        if (ToneMap.TryGetValue(targetVowel, out var tones))
        {
            char accentedVowel = tones[toneIndex];
            var sb = new StringBuilder(word);
            sb[targetPos] = accentedVowel;
            return sb.ToString();
        }

        return word;
    }
}
