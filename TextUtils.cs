using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace VietnameseSpellChecker;

public static class TextUtils
{

    public static string StripAccents(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var replaced = text.Replace('đ', 'd').Replace('Đ', 'D');
        var normalized = replaced.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public static bool HasAccent(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;
        return !string.Equals(word, StripAccents(word), StringComparison.Ordinal);
    }

    public static bool IsAllUpper(string word)
    {
        if (string.IsNullOrEmpty(word) || word.Length < 2) return false;
        return word.All(c => !char.IsLetter(c) || char.IsUpper(c));
    }

    public static bool IsCamelCase(string word)
    {
        if (string.IsNullOrEmpty(word) || word.Length <= 1) return false;
        return word.Any(char.IsUpper) && word.Any(char.IsLower) && word.Skip(1).Any(char.IsUpper);
    }


    public static string MatchCasing(string originalWord, string candidateWord)
    {
        if (string.IsNullOrEmpty(originalWord) || string.IsNullOrEmpty(candidateWord))
            return candidateWord;

        // Nếu từ ứng viên viết hoa toàn bộ (vd từ viết tắt chuẩn TPHCM, HTML, CPU)
        if (IsAllUpper(candidateWord) && !IsAllUpper(originalWord))
            return candidateWord;

        if (originalWord.Equals(candidateWord, StringComparison.OrdinalIgnoreCase))
            return originalWord;

        if (IsCamelCase(originalWord))
            return originalWord;

        if (IsAllUpper(originalWord))
            return candidateWord.ToUpper();

        if (char.IsUpper(originalWord[0]))
        {
            if (candidateWord.Length == 1)
                return char.ToUpper(candidateWord[0]).ToString();
            return char.ToUpper(candidateWord[0]) + candidateWord[1..].ToLower();
        }

        return candidateWord.ToLower();
    }
}
