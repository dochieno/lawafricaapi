using LawAfrica.API.Data;
using LawAfrica.API.Models.Ai;
using LawAfrica.API.Models.LawReportsContent.DTOs;
using LawAfrica.API.Models.LawReportsContent.Models;
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

            // ✅ ONE canonical string for:
            // - hashing
            // - sending to AI
            // - slicing ranges
            var normalized = NormalizeForAi(report.ContentText ?? "");
            if (string.IsNullOrWhiteSpace(normalized))
            {
                await ClearExistingAsync(lawReportId, ct);
                return (new BuildResult(lawReportId, Built: true, Hash: ComputeHash(""), BlocksWritten: 0), modelUsed: "n/a");
            }

            // ✅ IMPORTANT: truncate AFTER normalization so AI and slicing see the SAME exact string
            var maxChars = maxInputCharsOverride.GetValueOrDefault(20000);
            if (maxChars > 0 && normalized.Length > maxChars)
                normalized = normalized.Substring(0, maxChars);

            // ✅ Hash the exact string used for AI + builder version
            var hash = ComputeHash(normalized + "|" + BUILT_BYV1);

            var existingCache = await _db.Set<LawReportContentJsonCache>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.LawReportId == lawReportId, ct);

            if (!force && existingCache != null && string.Equals(existingCache.Hash, hash, StringComparison.Ordinal))
            {
                return (new BuildResult(lawReportId, Built: false, Hash: hash, BlocksWritten: 0), modelUsed: "cached");
            }

            // 1) Ask AI for ranges (ranges refer to *normalized*)
            var (ranges, modelUsed) = await _aiFormatter.FormatRangesAsync(normalized, ct);
            ranges = PostProcessAiRanges(ranges, normalized);

            // 2) Convert ranges -> ParsedBlock list (TEXT IS ALWAYS SLICED FROM *normalized*)
            var blocks = BuildBlocksFromRanges(normalized, ranges);
            blocks = FixTrailingCapsCategoryOnTitle(blocks);
            blocks = SplitParagraphBySectionMarkers(blocks);

            // 3) Persist (same as BuildAsync)
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
                    Type = b.Type, // DTO now exposes string "type" via TypeName, enum is JsonIgnored
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

        private static string NormalizeForAi(string input)
        {
            var text = (input ?? "")
                .Replace("\r\n", "\n")
                .Replace("\u00A0", " ")
                .Trim();

            // If flattened, salvage once
            text = SalvageNewlines(text);

            return text;
        }

        private static AiLawReportFormatResult PostProcessAiRanges(AiLawReportFormatResult result, string text)
        {
            if (result?.Blocks == null || result.Blocks.Count == 0) return result ?? new AiLawReportFormatResult();

            // 1) Clamp + ensure ordered + non-overlapping
            var blocks = result.Blocks
                .Where(b => b.End > b.Start)
                .Select(b => new AiLawReportRangeBlock
                {
                    Type = (b.Type ?? "paragraph").Trim(),
                    Start = Math.Max(0, b.Start),
                    End = Math.Min(text.Length, b.End),
                    Marker = b.Marker,
                    Indent = b.Indent
                })
                .Where(b => b.End > b.Start)
                .OrderBy(b => b.Start)
                .ToList();

            // 2) Fix overlaps by pushing starts forward
            int prevEnd = 0;
            foreach (var b in blocks)
            {
                if (b.Start < prevEnd) b.Start = prevEnd;
                if (b.End < b.Start + 1) b.End = Math.Min(text.Length, b.Start + 1);
                prevEnd = b.End;
            }

            // 3) Snap boundaries so we don’t split words (adjust the boundary between blocks)
            // We only move the boundary forward (safer) and set next start accordingly.
            const int lookAhead = 40;

            for (int i = 0; i < blocks.Count - 1; i++)
            {
                var a = blocks[i];
                var b = blocks[i + 1];

                int boundary = a.End;
                if (boundary <= 0 || boundary >= text.Length) continue;

                // If boundary splits a word: letter/digit on both sides => move boundary forward to a safe break
                if (IsWordChar(text[boundary - 1]) && IsWordChar(text[boundary]))
                {
                    int newBoundary = FindNextBreak(text, boundary, lookAhead);
                    if (newBoundary > boundary && newBoundary < b.End)
                    {
                        a.End = newBoundary;
                        b.Start = newBoundary;
                    }
                }
            }

            // 4) Retype “junk headings” as paragraphs (and remove bogus dividers)
            foreach (var b in blocks)
            {
                var t = (b.Type ?? "").ToLowerInvariant();

                if (t == "divider")
                {
                    var slice = text.Substring(b.Start, b.End - b.Start).Trim();
                    if (!LooksLikeDivider(slice)) b.Type = "paragraph";
                    continue;
                }

                if (t == "heading")
                {
                    var slice = text.Substring(b.Start, b.End - b.Start).Trim();
                    if (!LooksLikeHeading(slice))
                        b.Type = "paragraph";
                }
            }

            // 5) Merge consecutive paragraphs that are actually the same paragraph split badly
            blocks = MergeConsecutive(blocks, text, type: "paragraph");

            // 6) Fix Title + Meta: if “meta” starts with lowercase continuation, merge into title until newline
            blocks = FixTitleContinuation(blocks, text);

            return new AiLawReportFormatResult { Blocks = blocks };
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '\'' || c == '’';

        private static int FindNextBreak(string s, int start, int lookAhead)
        {
            int max = Math.Min(s.Length, start + lookAhead);
            for (int i = start; i < max; i++)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c) || c == '\n' || c == '\r' || c == '.' || c == ',' || c == ';' || c == ':' || c == ')' || c == ']')
                    return i; // boundary at break char
            }
            return start;
        }

        private static bool LooksLikeDivider(string slice)
        {
            if (string.IsNullOrWhiteSpace(slice)) return false;
            slice = slice.Trim();
            if (slice.Length < 3) return false;

            // allow lines like --- ___ ***
            return slice.All(ch => ch == '-' || ch == '_' || ch == '*');
        }

        private static bool LooksLikeHeading(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s.Length < 12)
            {
                // allow known short headings
                return IsKnownHeadingLabel(s);
            }

            // Must be mostly caps or a known label
            if (IsKnownHeadingLabel(s)) return true;

            // All-caps-ish heuristic (ignore punctuation/spaces)
            var letters = s.Where(char.IsLetter).ToList();
            if (letters.Count == 0) return false;
            var upper = letters.Count(char.IsUpper);
            return (upper / (double)letters.Count) > 0.85;
        }

        private static bool IsKnownHeadingLabel(string s)
        {
            var x = (s ?? "").Trim().ToUpperInvariant();
            return x is "RULING" or "JUDGMENT" or "JUDGEMENT" or "INTRODUCTION" or "BACKGROUND"
                or "FACTS" or "ISSUE" or "ISSUES" or "ANALYSIS" or "DETERMINATION" or "ORDERS" or "CONCLUSION";
        }

        private static List<AiLawReportRangeBlock> MergeConsecutive(List<AiLawReportRangeBlock> blocks, string text, string type)
        {
            var t = type.ToLowerInvariant();
            var outList = new List<AiLawReportRangeBlock>();
            foreach (var b in blocks)
            {
                if (outList.Count == 0) { outList.Add(b); continue; }

                var prev = outList[^1];
                if (string.Equals((prev.Type ?? "").ToLowerInvariant(), t) &&
                    string.Equals((b.Type ?? "").ToLowerInvariant(), t))
                {
                    // Merge if the gap is just whitespace/newlines
                    var gap = text.Substring(prev.End, Math.Max(0, b.Start - prev.End));
                    if (gap.All(ch => char.IsWhiteSpace(ch)))
                    {
                        prev.End = b.End;
                        outList[^1] = prev;
                        continue;
                    }
                }

                outList.Add(b);
            }
            return outList;
        }

        private static List<AiLawReportRangeBlock> FixTitleContinuation(List<AiLawReportRangeBlock> blocks, string text)
        {
            if (blocks.Count < 2) return blocks;

            var first = blocks[0];
            var second = blocks[1];

            if (!string.Equals(first.Type, "title", StringComparison.OrdinalIgnoreCase)) return blocks;
            if (!string.Equals(second.Type, "meta", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(second.Type, "metaline", StringComparison.OrdinalIgnoreCase))
                return blocks;

            // If meta starts with lowercase and title ends with word-char, it's probably a continuation
            if (second.Start < text.Length &&
                IsWordChar(text[Math.Max(0, first.End - 1)]) &&
                char.IsLower(text[second.Start]))
            {
                // extend title until first newline after second.Start (or safe break)
                int nl = text.IndexOf('\n', second.Start);
                if (nl > 0 && nl < second.End)
                {
                    first.End = nl;       // title ends at newline
                    second.Start = nl;    // meta starts at newline
                    blocks[0] = first;
                    blocks[1] = second;
                }
            }

            return blocks;
        }

        private List<ParsedBlock> FixTrailingCapsCategoryOnTitle(List<ParsedBlock> blocks)
        {
            if (blocks == null || blocks.Count == 0) return blocks;

            var first = blocks.FirstOrDefault();
            if (first == null || first.Type != LawReportContentBlockType.Title) return blocks;
            if (string.IsNullOrWhiteSpace(first.Text)) return blocks;

            var title = first.Text.Trim();

            // Split last token group if it's ALL CAPS and looks like a category
            // Example: "... Resort & Spa EMPLOYMENT" => title="... Resort & Spa", heading="EMPLOYMENT"
            var m = System.Text.RegularExpressions.Regex.Match(
                title,
                @"^(?<main>.+?)\s+(?<caps>[A-Z][A-Z\s/&\-]{2,30})$");

            if (!m.Success) return blocks;

            var main = m.Groups["main"].Value.Trim();
            var caps = m.Groups["caps"].Value.Trim();

            // Must be mostly caps letters
            var letters = caps.Where(char.IsLetter).ToList();
            if (letters.Count == 0) return blocks;
            if (letters.Count(c => char.IsUpper(c)) / (double)letters.Count < 0.95) return blocks;

            // Apply split
            first.Text = main;
            blocks[0] = first;

            // Insert a heading after title (order will be normalized later)
            blocks.Insert(1, new ParsedBlock
            {
                Type = LawReportContentBlockType.Heading,
                Text = caps,
                Style = "heading"
            });

            // Re-number orders
            for (int i = 0; i < blocks.Count; i++) blocks[i].Order = i + 1;

            return blocks;
        }
        private List<ParsedBlock> SplitParagraphBySectionMarkers(List<ParsedBlock> blocks)
        {
            if (blocks == null || blocks.Count == 0) return blocks;

            // Only work on paragraph blocks
            var markers = new[]
            {
        "Introduction",
        "Background",
        "Facts",
        "Issues",
        "Issues in dispute are:",
        "Analysis",
        "Analysis and Determination",
        "Determination",
        "Protection from arbitrary termination",
        "Orders",
        "Conclusion"
    };

            bool IsMarkerLine(string line)
            {
                var t = (line ?? "").Trim();
                if (t.Length < 4 || t.Length > 80) return false;

                // exact marker or startswith marker (case-insensitive)
                return markers.Any(m =>
                    t.Equals(m, StringComparison.OrdinalIgnoreCase) ||
                    t.StartsWith(m + " ", StringComparison.OrdinalIgnoreCase) ||
                    t.StartsWith(m + ":", StringComparison.OrdinalIgnoreCase));
            }

            var outList = new List<ParsedBlock>();

            foreach (var b in blocks)
            {
                if (b.Type != LawReportContentBlockType.Paragraph || string.IsNullOrWhiteSpace(b.Text))
                {
                    outList.Add(b);
                    continue;
                }

                // Split by newlines first
                var lines = b.Text.Replace("\r\n", "\n").Split('\n');

                // If no newlines, don't touch
                if (lines.Length <= 1)
                {
                    outList.Add(b);
                    continue;
                }

                var currentPara = new List<string>();

                void FlushPara()
                {
                    var joined = string.Join(" ", currentPara.Select(x => x.Trim()).Where(x => x.Length > 0)).Trim();
                    currentPara.Clear();
                    if (string.IsNullOrWhiteSpace(joined)) return;

                    outList.Add(new ParsedBlock
                    {
                        Type = LawReportContentBlockType.Paragraph,
                        Text = joined,
                        Style = "para"
                    });
                }

                foreach (var rawLine in lines)
                {
                    var line = (rawLine ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        FlushPara();
                        continue;
                    }

                    if (IsMarkerLine(line))
                    {
                        // end current paragraph
                        FlushPara();

                        // marker becomes heading
                        outList.Add(new ParsedBlock
                        {
                            Type = LawReportContentBlockType.Heading,
                            Text = line,
                            Style = "heading"
                        });

                        continue;
                    }

                    currentPara.Add(line);
                }

                FlushPara();
            }

            // Re-number orders
            for (int i = 0; i < outList.Count; i++)
                outList[i].Order = i + 1;

            return outList;
        }
    }
}