using System;
using System.Collections.Generic;

namespace VietnameseSpellChecker;

public static class PhoneticUtils
{
    private static readonly Dictionary<string, string[]> PrefixMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["s"] = ["x"],
        ["x"] = ["s"],
        ["tr"] = ["ch"],
        ["ch"] = ["tr"],
        ["d"] = ["gi", "r"],
        ["gi"] = ["d", "r"],
        ["r"] = ["d", "gi"],
        ["l"] = ["n"],
        ["n"] = ["l"]
    };

    public static List<string> GetPhoneticVariants(string word)
    {
        var variants = new List<string>();
        if (string.IsNullOrEmpty(word)) return variants;

        string lower = word.ToLower();

        // Kiểm tra tiền tố 2 ký tự trước (tr, ch, gi)
        if (lower.Length >= 2)
        {
            string p2 = lower[..2];
            if (PrefixMap.TryGetValue(p2, out var rep2))
            {
                string rest = lower[2..];
                foreach (var r in rep2)
                {
                    variants.Add(r + rest);
                }
                return variants;
            }
        }

        // Kiểm tra tiền tố 1 ký tự (s, x, d, r, l, n)
        if (lower.Length >= 1)
        {
            string p1 = lower[..1];
            if (PrefixMap.TryGetValue(p1, out var rep1))
            {
                string rest = lower[1..];
                foreach (var r in rep1)
                {
                    variants.Add(r + rest);
                }
            }
        }

        return variants;
    }
}
