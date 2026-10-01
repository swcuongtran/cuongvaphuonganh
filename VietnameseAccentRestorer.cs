using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace VietnameseSpellChecker;

public class VietnameseAccentRestorer
{
    private readonly NgramLanguageModel _lm;
    private static readonly Regex TokenizeRegex = new(@"([^\w]+)", RegexOptions.Compiled);
    private static readonly Regex WordRegex = new(@"^\w+$", RegexOptions.Compiled);

    public VietnameseAccentRestorer(NgramLanguageModel? lm = null)
    {
        _lm = lm ?? new NgramLanguageModel();
    }

    public (List<string> Tokens, List<bool> IsWordMask) Tokenize(string text)
    {
        var tokens = new List<string>();
        var isWordMask = new List<bool>();

        if (string.IsNullOrEmpty(text))
            return (tokens, isWordMask);

        var parts = TokenizeRegex.Split(text);
        foreach (var p in parts)
        {
            if (string.IsNullOrEmpty(p)) continue;

            tokens.Add(p);
            isWordMask.Add(WordRegex.IsMatch(p));
        }

        return (tokens, isWordMask);
    }

    public string Restore(string query, int beamWidth = 12)
    {
        var suggestions = Suggest(query, topK: 1, beamWidth: beamWidth);
        return suggestions.Count > 0 ? suggestions[0].Text : query;
    }

    public List<(string Text, double Score)> Suggest(string query, int topK = 5, int beamWidth = 12)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [(query, 0.0)];
        }

        var (allTokens, isWordMask) = Tokenize(query);
        var wordTokens = new List<string>();

        for (int i = 0; i < allTokens.Count; i++)
        {
            if (isWordMask[i])
            {
                wordTokens.Add(allTokens[i]);
            }
        }

        if (wordTokens.Count == 0)
        {
            return [(query, 0.0)];
        }

        // Lấy tập ứng viên cho từng từ
        var candidatesList = new List<(string OrigWord, List<Candidate> Candidates)>();
        foreach (var w in wordTokens)
        {
            var cands = _lm.GetCandidates(w);
            candidatesList.Add((w, cands));
        }

        // Beam Search Decoding
        var beam = new List<(double Score, List<Candidate> Seq)>
        {
            (0.0, new List<Candidate>())
        };

        foreach (var (origW, cands) in candidatesList)
        {
            var nextBeam = new List<(double Score, List<Candidate> Seq)>();

            foreach (var (currentScore, seq) in beam)
            {
                string? prev1 = seq.Count >= 1 ? seq[^1].Word : null;
                string? prev2 = seq.Count >= 2 ? seq[^2].Word : null;

                foreach (var cand in cands)
                {
                    double stepScore = _lm.ScoreNgram(prev2, prev1, cand, origW);
                    double newScore = currentScore + stepScore;

                    var newSeq = new List<Candidate>(seq.Count + 1);
                    newSeq.AddRange(seq);
                    newSeq.Add(cand);

                    nextBeam.Add((newScore, newSeq));
                }
            }

            // Sắp xếp giảm dần theo điểm và chọn top beamWidth
            nextBeam.Sort((a, b) => b.Score.CompareTo(a.Score));

            if (nextBeam.Count > beamWidth)
            {
                beam = nextBeam.GetRange(0, beamWidth);
            }
            else
            {
                beam = nextBeam;
            }
        }

        // Ghép lại kết quả theo định dạng chữ hoa/thường, số và dấu câu gốc
        var results = new List<(string Text, double Score)>();
        int countToTake = Math.Min(topK, beam.Count);

        for (int b = 0; b < countToTake; b++)
        {
            var (score, seq) = beam[b];

            var formattedWords = new List<string>(seq.Count);
            for (int i = 0; i < seq.Count; i++)
            {
                string origW = candidatesList[i].OrigWord;
                string candW = seq[i].Word;
                formattedWords.Add(TextUtils.MatchCasing(origW, candW));
            }

            var sb = new StringBuilder();
            int wordIdx = 0;

            for (int i = 0; i < allTokens.Count; i++)
            {
                if (isWordMask[i])
                {
                    sb.Append(formattedWords[wordIdx++]);
                }
                else
                {
                    sb.Append(allTokens[i]);
                }
            }

            results.Add((sb.ToString(), Math.Round(score, 2)));
        }

        return results;
    }
}
