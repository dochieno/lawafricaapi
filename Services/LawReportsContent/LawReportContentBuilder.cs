using LawAfrica.API.Data;
using LawAfrica.API.Models.Ai;
using LawAfrica.API.Models.LawReportsContent;
using LawAfrica.API.Models.Payments.LawReportsContent.Models;
using LawAfrica.API.Services.Ai;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
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
        private readonly ILawReportFormatter _aiFormatter;

        // bump this when you change parsing rules
        private const string BUILT_BY = "lawreport-block-builder:v2";
        private const string BUILT_BYV1 = "lawreport-block-builder:v3+ai-ranges";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public LawReportContentBuilder(ApplicationDbContext db, ILawReportFormatter aiFormatter)
        {
            _db = db;
            _aiFormatter = aiFormatter;
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

        //AI -powered builder
        public async Task<(BuildResult result, string modelUsed)> BuildAiAsync(
        int lawReportId,
        bool force = false,
        int? maxInputCharsOverride = null,
        CancellationToken ct = default)
            {
                var report = await _db.LawReports
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == lawReportId, ct);

                if (report == null)
                    throw new InvalidOperationException("Law report not found.");

                var raw = (report.ContentText ?? "").Replace("\r\n", "\n").Trim();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    await ClearExistingAsync(lawReportId, ct);
                    return (new BuildResult(lawReportId, Built: true, Hash: ComputeHash(""), BlocksWritten: 0), modelUsed: "n/a");
                }

                // optional truncation before AI (still safe; you’ll format what you sent)
                var maxInputChars = maxInputCharsOverride.GetValueOrDefault(20000);
                var sendToAi = raw;
                if (sendToAi.Length > maxInputChars)
                    sendToAi = sendToAi.Substring(0, maxInputChars);

                var hash = ComputeHash(sendToAi + "|" + BUILT_BYV1);

                var existingCache = await _db.Set<LawReportContentJsonCache>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.LawReportId == lawReportId, ct);

                if (!force && existingCache != null && string.Equals(existingCache.Hash, hash, StringComparison.Ordinal))
                {
                    return (new BuildResult(lawReportId, Built: false, Hash: hash, BlocksWritten: 0), modelUsed: "cached");
                }

                // 1) Ask AI for ranges
                var (ranges, modelUsed) = await _aiFormatter.FormatRangesAsync(sendToAi, ct);

                // 2) Convert ranges -> ParsedBlock list (TEXT IS ALWAYS SLICED FROM ORIGINAL)
                var blocks = BuildBlocksFromRanges(sendToAi, ranges);

                // 3) Persist (same as your BuildAsync)
                await using var tx = await _db.Database.BeginTransactionAsync(ct);

                var oldBlocks = await _db.Set<LawReportContentBlock>()
                    .Where(x => x.LawReportId == lawReportId)
                    .ToListAsync(ct);

                if (oldBlocks.Count > 0)
                {
                    _db.RemoveRange(oldBlocks);
                    await _db.SaveChangesAsync(ct);
                }

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
                        BuiltBy = BUILT_BYV1,
                        BuiltAt = DateTime.UtcNow
                    };
                    _db.Add(cache);
                }
                else
                {
                    cache.Hash = hash;
                    cache.Json = jsonString;
                    cache.BuiltBy = BUILT_BYV1;
                    cache.UpdatedAt = DateTime.UtcNow;
                }

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return (new BuildResult(lawReportId, Built: true, Hash: hash, BlocksWritten: blocks.Count), modelUsed);
            }

        private List<ParsedBlock> BuildBlocksFromRanges(string input, AiLawReportFormatResult ranges)
        {
            var blocks = new List<ParsedBlock>();
            int order = 1;

            foreach (var r in ranges.Blocks)
            {
                var slice = input.Substring(r.Start, r.End - r.Start);
                var text = slice.Trim(); // keep clean; still exact words from input, just trimming edges

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                switch ((r.Type ?? "").Trim().ToLowerInvariant())
                {
                    case "title":
                        blocks.Add(new ParsedBlock
                        {
                            Order = order++,
                            Type = LawReportContentBlockType.Title,
                            Text = text,
                            Style = "title"
                        });
                        break;

                    case "meta":
                        blocks.Add(new ParsedBlock
                        {
                            Order = order++,
                            Type = LawReportContentBlockType.MetaLine,
                            Text = text,
                            Json = JsonSerializer.Serialize(new { text }, JsonOpts),
                            Style = "meta"
                        });
                        break;

                    case "heading":
                        blocks.Add(new ParsedBlock
                        {
                            Order = order++,
                            Type = LawReportContentBlockType.Heading,
                            Text = NormalizeHeading(text),
                            Style = "heading"
                        });
                        break;

                    case "list_item":
                        {
                            var marker = (r.Marker ?? "").Trim();
                            var itemText = text;

                            // If marker not provided, try to detect from slice prefix (safe: does not change words)
                            if (string.IsNullOrWhiteSpace(marker))
                            {
                                if (IsListItemLine(text, out var m, out var v))
                                {
                                    marker = m;
                                    itemText = v;
                                }
                            }
                            else
                            {
                                // If marker is provided, remove it from displayed "text" only if it’s actually there
                                if (itemText.StartsWith(marker))
                                    itemText = itemText.Substring(marker.Length).TrimStart();
                            }

                            var display = string.IsNullOrWhiteSpace(marker) ? itemText : $"{marker} {itemText}".Trim();

                            blocks.Add(new ParsedBlock
                            {
                                Order = order++,
                                Type = LawReportContentBlockType.ListItem,
                                Text = display,
                                Json = JsonSerializer.Serialize(new { marker, text = itemText }, JsonOpts),
                                Indent = r.Indent ?? 0,
                                Style = "list"
                            });
                        }
                        break;

                    case "divider":
                        blocks.Add(new ParsedBlock
                        {
                            Order = order++,
                            Type = LawReportContentBlockType.Divider,
                            Text = null,
                            Style = "divider"
                        });
                        break;

                    case "spacer":
                        blocks.Add(new ParsedBlock
                        {
                            Order = order++,
                            Type = LawReportContentBlockType.Spacer,
                            Text = null,
                            Style = "spacer"
                        });
                        break;

                    default:
                        blocks.Add(new ParsedBlock
                        {
                            Order = order++,
                            Type = LawReportContentBlockType.Paragraph,
                            Text = text,
                            Style = "para"
                        });
                        break;
                }
            }

            // Ensure we always have at least one paragraph if AI returned only meta/headings accidentally
            if (blocks.Count == 0)
            {
                blocks.Add(new ParsedBlock
                {
                    Order = 1,
                    Type = LawReportContentBlockType.Paragraph,
                    Text = input.Trim(),
                    Style = "para"
                });
            }

            return blocks;
        }//End of AI Builder

        // =========================
        // Parsing rules (v2)
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
            var text = (input ?? "").Replace("\r\n", "\n").Replace("\u00A0", " ");

            // ✅ If content came in as ONE giant line, inject breaks using common markers
            text = SalvageNewlines(text);

            // split into lines to detect “Lexis-like” front matter
            var lines = text.Split('\n').Select(x => x.TrimEnd()).ToList();

            var blocks = new List<ParsedBlock>();
            int order = 1;

            // --- 1) Front matter: first ~80 lines until a blank line or a body paragraph ---
            var front = new List<string>();
            int i = 0;

            for (; i < lines.Count && i < 80; i++)
            {
                var ln = lines[i].Trim();

                if (string.IsNullOrWhiteSpace(ln))
                {
                    if (front.Count > 0) break;
                    continue;
                }

                // break into body once we hit a long paragraph-ish line (front already started)
                if (ln.Length > 160 && front.Count > 0) break;

                front.Add(ln);

                if (IsHeadingLine(ln)) { i++; break; }
            }

            // Title heuristic: first front line that looks like “A v B”
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
                foreach (var m in front.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    var isHead = IsHeadingLine(m) || IsAllCapsHeading(m);
                    blocks.Add(new ParsedBlock
                    {
                        Order = order++,
                        Type = isHead ? LawReportContentBlockType.Heading : LawReportContentBlockType.MetaLine,
                        Text = isHead ? NormalizeHeading(m) : m,
                        Json = isHead ? null : JsonSerializer.Serialize(new { text = m }, JsonOpts),
                        Style = isHead ? "heading" : "meta"
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

            // --- 2) Body parsing: paragraphs + list items ---
            var bodyLines = lines.Skip(i).ToList();
            var para = new List<string>();

            void flushPara()
            {
                if (para.Count == 0) return;

                var joined = string.Join(" ", para.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
                para.Clear();

                if (string.IsNullOrWhiteSpace(joined)) return;

                if (IsAllCapsHeading(joined) || IsHeadingLine(joined))
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
        // Salvage helper (important)
        // =========================

        private static string SalvageNewlines(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // If there are already newlines, don't touch.
            if (text.Contains('\n')) return text;

            // Only salvage if it looks like a giant flattened document
            if (text.Length < 500) return text;

            // Add breaks around common law report markers
            var t = text;

            // Court markers
            t = Regex.Replace(t, @"\s+(HIGH\s+COURT\s+OF\s+KENYA)\s+", "\n$1 ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\s+(COURT\s+OF\s+APPEAL\s+AT\s+[A-Z][A-Z\s]+)\s+", "\n$1 ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\s+(SUPREME\s+COURT\s+OF\s+KENYA)\s+", "\n$1 ", RegexOptions.IgnoreCase);

            // Judges / date / case no / citation
            t = Regex.Replace(t, @"\s+(Date\s+of\s+Judg(?:ment|ement))\s+", "\n$1 ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\s+(Case\s+No\.?)\s*", "\n$1 ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\s+(Sourced\s+by)\s+", "\n$1 ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\s+(Citation)\s+", "\n$1 ", RegexOptions.IgnoreCase);

            // Judgment heading (handles "J u d g m e n t")
            t = Regex.Replace(t, @"\s+(J\s*u\s*d\s*g\s*m\s*e\s*n\s*t)\s+", "\nJUDGMENT\n", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\s+(JUDGMENT|JUDGEMENT|RULING)\s+", "\n$1\n", RegexOptions.IgnoreCase);

            // Start paragraph split after periods for readability (very conservative)
            t = Regex.Replace(t, @"\.\s+(On\s+\d{1,2}\s+[A-Za-z]+\s*,?\s+\d{4})", ".\n\n$1", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\.\s+(It\s+is\s+expressed\s+to\s+be)", ".\n\n$1", RegexOptions.IgnoreCase);

            return t.Trim();
        }

        // =========================
        // Helpers
        // =========================

        private static bool IsCaseTitleLine(string s)
        {
            var x = (s ?? "").Trim();
            if (x.Length < 8) return false;
            if (x.Length > 140) return false; // ✅ prevent huge single-line docs becoming "title"

            // require " v " or " vs " as a word separator, not just any 'v' anywhere
            if (Regex.IsMatch(x, @"\b(v|vs|versus)\b", RegexOptions.IgnoreCase))
                return true;

            return false;
        }

        private static bool IsHeadingLine(string s)
        {
            var x = (s ?? "").Trim();
            if (x.Length == 0) return false;

            return Regex.IsMatch(x,
                @"^(RULING|JUDGMENT|JUDGEMENT|INTRODUCTION|BACKGROUND|FACTS?|ISSUES?|ANALYSIS|DETERMINATION|ORDERS?|CONCLUSION)\b",
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
            x = Regex.Replace(x, @"\s{2,}", " ");
            return x;
        }

        private static bool IsListItemLine(string s, out string marker, out string value)
        {
            marker = "";
            value = "";

            var m1 = Regex.Match(s, @"^(?<m>\d{1,2}\))\s*(?<t>.+)$");
            if (m1.Success)
            {
                marker = m1.Groups["m"].Value;
                value = m1.Groups["t"].Value.Trim();
                return true;
            }

            var m2 = Regex.Match(s, @"^(?<m>\d{1,2}\.)\s*(?<t>.+)$");
            if (m2.Success)
            {
                marker = m2.Groups["m"].Value;
                value = m2.Groups["t"].Value.Trim();
                return true;
            }

            var m3 = Regex.Match(s, @"^(?<m>\([a-z]\))\s*(?<t>.+)$", RegexOptions.IgnoreCase);
            if (m3.Success)
            {
                marker = m3.Groups["m"].Value;
                value = m3.Groups["t"].Value.Trim();
                return true;
            }

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
        public async Task<LawReportContentJsonDto> GetJsonDtoAsync(int lawReportId, CancellationToken ct = default)
        {
            var cache = await _db.Set<LawReportContentJsonCache>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.LawReportId == lawReportId, ct);

            if (cache == null || string.IsNullOrWhiteSpace(cache.Json))
                return new LawReportContentJsonDto { LawReportId = lawReportId, Hash = null, Blocks = new() };

            return JsonSerializer.Deserialize<LawReportContentJsonDto>(cache.Json, JsonOpts)
                ?? new LawReportContentJsonDto { LawReportId = lawReportId, Hash = cache.Hash, Blocks = new() };
        }
    }
}