using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.LawReportsContent;
using LawAfrica.API.Models.Payments.LawReportsContent.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LawAfrica.API.Services.LawReportsContent
{
    public class LawReportContentBuilder : ILawReportContentBuilder
    {
        private readonly ApplicationDbContext _db;

        // bump this when you change parsing rules
        private const string BUILT_BY = "lawreport-block-builder:v1";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public LawReportContentBuilder(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<BuildResult> BuildAsync(int lawReportId, bool force = false, CancellationToken ct = default)
        {
            // 1) Load report content
            var report = await _db.LawReports
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == lawReportId, ct);

            if (report == null)
                throw new InvalidOperationException("Law report not found.");

            var raw = (report.ContentText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                // still clear existing blocks/cache to avoid stale rendering
                await ClearExistingAsync(lawReportId, ct);
                return new BuildResult(lawReportId, Built: true, Hash: ComputeHash(""), BlocksWritten: 0);
            }

            // 2) Hash (content + builder version)
            var hash = ComputeHash(raw + "|" + BUILT_BY);

            // 3) If cache exists and same hash and !force => no-op
            var existingCache = await _db.Set<LawReportContentJsonCache>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.LawReportId == lawReportId, ct);

            if (!force && existingCache != null && string.Equals(existingCache.Hash, hash, StringComparison.Ordinal))
            {
                return new BuildResult(lawReportId, Built: false, Hash: hash, BlocksWritten: 0);
            }

            // 4) Parse blocks
            var blocks = Parse(raw);

            // 5) Persist: replace blocks + upsert cache
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            // delete existing blocks
            var oldBlocks = await _db.Set<LawReportContentBlock>()
                .Where(x => x.LawReportId == lawReportId)
                .ToListAsync(ct);

            if (oldBlocks.Count > 0)
            {
                _db.RemoveRange(oldBlocks);
                await _db.SaveChangesAsync(ct);
            }

            // insert new blocks
            _db.AddRange(blocks.Select(b => new LawReportContentBlock
            {
                LawReportId = lawReportId,
                Order = b.Order,
                Type = b.Type,
                Text = b.Text,
                Json = b.Json,
                Indent = b.Indent,
                Style = b.Style,
                CreatedAt = DateTime.UtcNow
            }));

            await _db.SaveChangesAsync(ct);

            // build final JSON cache payload
            var jsonDto = new LawReportContentJsonDto
            {
                LawReportId = lawReportId,
                Hash = hash,
                Blocks = blocks.Select(b => new LawReportContentJsonBlockDto
                {
                    Order = b.Order,
                    Type = b.Type,
                    Text = b.Text,
                    Data = string.IsNullOrWhiteSpace(b.Json) ? null : JsonSerializer.Deserialize<object>(b.Json!, JsonOpts),
                    Indent = b.Indent,
                    Style = b.Style
                }).ToList()
            };

            var jsonString = JsonSerializer.Serialize(jsonDto, JsonOpts);

            var cache = await _db.Set<LawReportContentJsonCache>()
                .FirstOrDefaultAsync(x => x.LawReportId == lawReportId, ct);

            if (cache == null)
            {
                cache = new LawReportContentJsonCache
                {
                    LawReportId = lawReportId,
                    Hash = hash,
                    Json = jsonString,
                    BuiltBy = BUILT_BY,
                    BuiltAt = DateTime.UtcNow
                };
                _db.Add(cache);
            }
            else
            {
                cache.Hash = hash;
                cache.Json = jsonString;
                cache.BuiltBy = BUILT_BY;
                cache.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new BuildResult(lawReportId, Built: true, Hash: hash, BlocksWritten: blocks.Count);
        }

        private async Task ClearExistingAsync(int lawReportId, CancellationToken ct)
        {
            var oldBlocks = await _db.Set<LawReportContentBlock>()
                .Where(x => x.LawReportId == lawReportId)
                .ToListAsync(ct);

            if (oldBlocks.Count > 0)
            {
                _db.RemoveRange(oldBlocks);
                await _db.SaveChangesAsync(ct);
            }

            var cache = await _db.Set<LawReportContentJsonCache>()
                .FirstOrDefaultAsync(x => x.LawReportId == lawReportId, ct);

            if (cache != null)
            {
                _db.Remove(cache);
                await _db.SaveChangesAsync(ct);
            }
        }

        // =========================
        // Parsing rules (v1)
        // =========================

        private class ParsedBlock
        {
            public int Order { get; set; }
            public LawReportContentBlockType Type { get; set; }
            public string? Text { get; set; }
            public string? Json { get; set; }
            public int? Indent { get; set; }
            public string? Style { get; set; }
        }

        private List<ParsedBlock> Parse(string input)
        {
            // normalize newlines, preserve blank lines
            var text = input.Replace("\r\n", "\n").Replace("\u00A0", " ");

            // split into lines to detect “Lexis-like” front matter
            var lines = text.Split('\n').Select(x => x.TrimEnd()).ToList();

            var blocks = new List<ParsedBlock>();
            int order = 1;

            // --- 1) Front matter: first ~25 lines until a big gap or “Judgment/Ruling/…” ---
            // We convert these into:
            // - Title (case name line)
            // - Meta lines (court, judge, date, citation, etc.)
            // - Heading (RULING/JUDGMENT/etc.)
            var front = new List<string>();
            int i = 0;

            for (; i < lines.Count && i < 80; i++)
            {
                var ln = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(ln))
                {
                    // stop front matter after first “real” blank line *once we already have some*
                    if (front.Count > 0) break;
                    continue;
                }

                // break into body once we hit long paragraph-ish line
                if (ln.Length > 120 && front.Count > 0) break;

                front.Add(ln);

                // if we hit a known heading, stop front collection
                if (IsHeadingLine(ln)) { i++; break; }
            }

            // Title heuristic: first front line that looks like “A v B …”
            var titleLineIndex = front.FindIndex(IsCaseTitleLine);
            if (titleLineIndex >= 0)
            {
                blocks.Add(new ParsedBlock
                {
                    Order = order++,
                    Type = LawReportContentBlockType.Title,
                    Text = front[titleLineIndex],
                    Style = "title"
                });

                // all other front lines become meta unless they are headings
                var metaLines = front
                    .Where((x, idx) => idx != titleLineIndex)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Where(x => !IsHeadingLine(x))
                    .ToList();

                foreach (var m in metaLines)
                {
                    blocks.Add(new ParsedBlock
                    {
                        Order = order++,
                        Type = LawReportContentBlockType.MetaLine,
                        Text = m,
                        Json = JsonSerializer.Serialize(new { text = m }, JsonOpts),
                        Style = "meta"
                    });
                }
            }
            else
            {
                // no clear title, treat all front lines as meta
                foreach (var m in front.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    blocks.Add(new ParsedBlock
                    {
                        Order = order++,
                        Type = IsHeadingLine(m) ? LawReportContentBlockType.Heading : LawReportContentBlockType.MetaLine,
                        Text = m,
                        Json = IsHeadingLine(m) ? null : JsonSerializer.Serialize(new { text = m }, JsonOpts),
                        Style = IsHeadingLine(m) ? "heading" : "meta"
                    });
                }
            }

            // if we stopped because next line is a heading, add it
            if (i > 0 && i <= lines.Count - 1)
            {
                var maybeHeading = lines[Math.Max(0, i - 1)].Trim();
                if (IsHeadingLine(maybeHeading))
                {
                    blocks.Add(new ParsedBlock
                    {
                        Order = order++,
                        Type = LawReportContentBlockType.Heading,
                        Text = NormalizeHeading(maybeHeading),
                        Style = "heading"
                    });
                }
            }

            // --- 2) Body parsing: paragraphs + numbered/lettered items like screen 2 ---
            // We'll build paragraphs from line runs separated by blanks.
            var bodyLines = lines.Skip(i).ToList();
            var para = new List<string>();

            void flushPara()
            {
                if (para.Count == 0) return;
                var joined = string.Join(" ", para.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
                para.Clear();
                if (string.IsNullOrWhiteSpace(joined)) return;

                // if paragraph is all-caps short => heading
                if (IsAllCapsHeading(joined))
                {
                    blocks.Add(new ParsedBlock
                    {
                        Order = order++,
                        Type = LawReportContentBlockType.Heading,
                        Text = NormalizeHeading(joined),
                        Style = "heading"
                    });
                    return;
                }

                blocks.Add(new ParsedBlock
                {
                    Order = order++,
                    Type = LawReportContentBlockType.Paragraph,
                    Text = joined,
                    Style = "para"
                });
            }

            foreach (var rawLine in bodyLines)
            {
                var ln = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(ln))
                {
                    flushPara();
                    continue;
                }

                // heading line mid-body
                if (IsHeadingLine(ln) || IsAllCapsHeading(ln))
                {
                    flushPara();
                    blocks.Add(new ParsedBlock
                    {
                        Order = order++,
                        Type = LawReportContentBlockType.Heading,
                        Text = NormalizeHeading(ln),
                        Style = "heading"
                    });
                    continue;
                }

                // numbered item 1) / 1. / (a) / a)
                if (IsListItemLine(ln, out var marker, out var value))
                {
                    flushPara();
                    blocks.Add(new ParsedBlock
                    {
                        Order = order++,
                        Type = LawReportContentBlockType.ListItem,
                        Text = $"{marker} {value}".Trim(),
                        Json = JsonSerializer.Serialize(new { marker, text = value }, JsonOpts),
                        Indent = 0,
                        Style = "list"
                    });
                    continue;
                }

                para.Add(ln);
            }

            flushPara();

            return blocks;
        }

        // =========================
        // Helpers
        // =========================

        private static bool IsCaseTitleLine(string s)
        {
            // Typical: "Odundo v Africa Merchant Assurance Company Limited"
            var x = (s ?? "").Trim();
            if (x.Length < 8) return false;
            if (Regex.IsMatch(x, @"\bv\b", RegexOptions.IgnoreCase)) return true;
            return false;
        }

        private static bool IsHeadingLine(string s)
        {
            var x = (s ?? "").Trim();
            if (x.Length == 0) return false;

            // common law report headings
            return Regex.IsMatch(x, @"^(RULING|JUDGMENT|JUDGEMENT|INTRODUCTION|BACKGROUND|FACTS?|ISSUES?|ANALYSIS|DETERMINATION|ORDERS?|CONCLUSION)\b",
                RegexOptions.IgnoreCase);
        }

        private static bool IsAllCapsHeading(string s)
        {
            var x = (s ?? "").Trim();
            if (x.Length < 3 || x.Length > 60) return false;
            return Regex.IsMatch(x, @"^[A-Z\s.,'’\-()]+$") && Regex.IsMatch(x, @"[A-Z]");
        }

        private static string NormalizeHeading(string s)
        {
            var x = (s ?? "").Trim();
            // normalize double spaces
            x = Regex.Replace(x, @"\s{2,}", " ");
            return x;
        }

        private static bool IsListItemLine(string s, out string marker, out string value)
        {
            marker = "";
            value = "";

            // 1) text
            var m1 = Regex.Match(s, @"^(?<m>\d{1,2}\))\s*(?<t>.+)$");
            if (m1.Success)
            {
                marker = m1.Groups["m"].Value;
                value = m1.Groups["t"].Value.Trim();
                return true;
            }

            // 1. text
            var m2 = Regex.Match(s, @"^(?<m>\d{1,2}\.)\s*(?<t>.+)$");
            if (m2.Success)
            {
                marker = m2.Groups["m"].Value;
                value = m2.Groups["t"].Value.Trim();
                return true;
            }

            // (a) text
            var m3 = Regex.Match(s, @"^(?<m>\([a-z]\))\s*(?<t>.+)$", RegexOptions.IgnoreCase);
            if (m3.Success)
            {
                marker = m3.Groups["m"].Value;
                value = m3.Groups["t"].Value.Trim();
                return true;
            }

            // a) text
            var m4 = Regex.Match(s, @"^(?<m>[a-z]\))\s*(?<t>.+)$", RegexOptions.IgnoreCase);
            if (m4.Success)
            {
                marker = m4.Groups["m"].Value;
                value = m4.Groups["t"].Value.Trim();
                return true;
            }

            return false;
        }

        private static string ComputeHash(string s)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(s ?? "");
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}