using LawAfrica.API.Data;
using LawAfrica.API.Models.Documents;
using LawAfrica.API.Models.DTOs.LegalDocuments.Toc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Documents
{
    public class LegalDocumentTocService
    {
        private readonly ApplicationDbContext _db;

        public LegalDocumentTocService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<List<TocEntryDto>> GetTreeAsync(int legalDocumentId, bool includeAdminFields)
        {
            var rows = await _db.LegalDocumentTocEntries
                .AsNoTracking()
                .Where(x => x.LegalDocumentId == legalDocumentId)
                .OrderBy(x => x.ParentId)
                .ThenBy(x => x.Order)
                .ToListAsync();

            var map = rows.ToDictionary(
                r => r.Id,
                r => new TocEntryDto
                {
                    Id = r.Id,
                    ParentId = r.ParentId,
                    Title = r.Title,
                    Level = r.Level,
                    Order = r.Order,
                    TargetType = r.TargetType,
                    StartPage = r.StartPage,
                    EndPage = r.EndPage,
                    AnchorId = r.AnchorId,
                    PageLabel = r.PageLabel,
                    Notes = includeAdminFields ? r.Notes : null,
                    Children = new List<TocEntryDto>()
                });

            var roots = new List<TocEntryDto>();
            foreach (var dto in map.Values.OrderBy(x => x.Order))
            {
                if (dto.ParentId == null)
                {
                    roots.Add(dto);
                }
                else if (map.TryGetValue(dto.ParentId.Value, out var parent))
                {
                    parent.Children.Add(dto);
                }
                else
                {
                    // orphan -> treat as root
                    roots.Add(dto);
                }
            }

            // Ensure children ordered
            void SortChildren(List<TocEntryDto> nodes)
            {
                nodes.Sort((a, b) => a.Order.CompareTo(b.Order));
                foreach (var n in nodes) SortChildren(n.Children);
            }
            SortChildren(roots);

            return roots;
        }

        public async Task<TocEntryDto> CreateAsync(int legalDocumentId, TocEntryCreateRequest req)
        {
            await ValidateAsync(legalDocumentId, req.TargetType, req.StartPage, req.EndPage, req.AnchorId);

            var nextOrder = req.Order ?? await GetNextOrderAsync(legalDocumentId, req.ParentId);

            var entity = new LegalDocumentTocEntry
            {
                LegalDocumentId = legalDocumentId,
                ParentId = req.ParentId,
                Title = req.Title.Trim(),
                Level = req.Level,
                Order = nextOrder,
                TargetType = req.TargetType,
                StartPage = req.StartPage,
                EndPage = req.EndPage,
                AnchorId = string.IsNullOrWhiteSpace(req.AnchorId) ? null : req.AnchorId.Trim(),
                PageLabel = string.IsNullOrWhiteSpace(req.PageLabel) ? null : req.PageLabel.Trim(),
                Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            _db.LegalDocumentTocEntries.Add(entity);
            await _db.SaveChangesAsync();

            return new TocEntryDto
            {
                Id = entity.Id,
                ParentId = entity.ParentId,
                Title = entity.Title,
                Level = entity.Level,
                Order = entity.Order,
                TargetType = entity.TargetType,
                StartPage = entity.StartPage,
                EndPage = entity.EndPage,
                AnchorId = entity.AnchorId,
                PageLabel = entity.PageLabel,
                Notes = entity.Notes
            };
        }

        public async Task<TocEntryDto> UpdateAsync(int legalDocumentId, int entryId, TocEntryUpdateRequest req)
        {
            var entity = await _db.LegalDocumentTocEntries
                .FirstOrDefaultAsync(x => x.Id == entryId && x.LegalDocumentId == legalDocumentId);

            if (entity == null) throw new KeyNotFoundException("ToC entry not found.");

            await ValidateAsync(legalDocumentId, req.TargetType, req.StartPage, req.EndPage, req.AnchorId);

            entity.Title = req.Title.Trim();
            entity.Level = req.Level;
            entity.TargetType = req.TargetType;
            entity.StartPage = req.StartPage;
            entity.EndPage = req.EndPage;
            entity.AnchorId = string.IsNullOrWhiteSpace(req.AnchorId) ? null : req.AnchorId.Trim();
            entity.PageLabel = string.IsNullOrWhiteSpace(req.PageLabel) ? null : req.PageLabel.Trim();
            entity.Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
            entity.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return new TocEntryDto
            {
                Id = entity.Id,
                ParentId = entity.ParentId,
                Title = entity.Title,
                Level = entity.Level,
                Order = entity.Order,
                TargetType = entity.TargetType,
                StartPage = entity.StartPage,
                EndPage = entity.EndPage,
                AnchorId = entity.AnchorId,
                PageLabel = entity.PageLabel,
                Notes = entity.Notes
            };
        }

        public async Task DeleteAsync(int legalDocumentId, int entryId)
        {
            var entity = await _db.LegalDocumentTocEntries
                .Include(x => x.Children)
                .FirstOrDefaultAsync(x => x.Id == entryId && x.LegalDocumentId == legalDocumentId);

            if (entity == null) return;

            // If you prefer cascade delete on children, implement recursive delete.
            // For now: block deletion if it has children (safer for admin).
            if (entity.Children.Any())
                throw new InvalidOperationException("Cannot delete a ToC entry that has children. Delete children first.");

            _db.LegalDocumentTocEntries.Remove(entity);
            await _db.SaveChangesAsync();
        }

        public async Task ReorderAsync(int legalDocumentId, TocReorderRequest req)
        {
            if (req.Items == null || req.Items.Count == 0) return;

            var ids = req.Items.Select(x => x.Id).Distinct().ToList();

            var entities = await _db.LegalDocumentTocEntries
                .Where(x => x.LegalDocumentId == legalDocumentId && ids.Contains(x.Id))
                .ToListAsync();

            var map = entities.ToDictionary(x => x.Id);

            foreach (var item in req.Items)
            {
                if (!map.TryGetValue(item.Id, out var e)) continue;

                // prevent self-parenting
                if (item.ParentId == item.Id)
                    throw new InvalidOperationException("Invalid ParentId (cannot be self).");

                e.ParentId = item.ParentId;
                e.Order = item.Order;
                e.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
        }

        public async Task ImportAsync(int legalDocumentId, TocImportRequest req)
        {
            var mode = (req.Mode ?? "replace").Trim().ToLowerInvariant();
            if (mode != "replace" && mode != "append")
                throw new InvalidOperationException("Mode must be 'replace' or 'append'.");

            if (mode == "replace")
            {
                var existing = await _db.LegalDocumentTocEntries
                    .Where(x => x.LegalDocumentId == legalDocumentId)
                    .ToListAsync();

                _db.LegalDocumentTocEntries.RemoveRange(existing);
                await _db.SaveChangesAsync();
            }

            // Create rows in two passes to resolve ParentClientId
            var items = req.Items ?? new List<TocImportItem>();
            if (items.Count == 0) return;

            // validate each item destination
            foreach (var it in items)
                await ValidateAsync(legalDocumentId, it.TargetType, it.StartPage, it.EndPage, it.AnchorId);

            var clientIdToEntity = new Dictionary<string, LegalDocumentTocEntry>();

            // pass 1: create entities (without ParentId)
            foreach (var it in items.OrderBy(x => x.Order))
            {
                var e = new LegalDocumentTocEntry
                {
                    LegalDocumentId = legalDocumentId,
                    Title = it.Title.Trim(),
                    Level = it.Level,
                    Order = it.Order,
                    TargetType = it.TargetType,
                    StartPage = it.StartPage,
                    EndPage = it.EndPage,
                    AnchorId = string.IsNullOrWhiteSpace(it.AnchorId) ? null : it.AnchorId.Trim(),
                    PageLabel = string.IsNullOrWhiteSpace(it.PageLabel) ? null : it.PageLabel.Trim(),
                    Notes = string.IsNullOrWhiteSpace(it.Notes) ? null : it.Notes.Trim(),
                    CreatedAt = DateTime.UtcNow
                };

                _db.LegalDocumentTocEntries.Add(e);
                clientIdToEntity[it.ClientId] = e;
            }

            await _db.SaveChangesAsync();

            // pass 2: set ParentId
            foreach (var it in items)
            {
                if (string.IsNullOrWhiteSpace(it.ParentClientId)) continue;

                if (!clientIdToEntity.TryGetValue(it.ClientId, out var child)) continue;
                if (!clientIdToEntity.TryGetValue(it.ParentClientId!, out var parent)) continue;

                child.ParentId = parent.Id;
                child.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
        }

        private async Task<int> GetNextOrderAsync(int legalDocumentId, int? parentId)
        {
            var max = await _db.LegalDocumentTocEntries
                .Where(x => x.LegalDocumentId == legalDocumentId && x.ParentId == parentId)
                .Select(x => (int?)x.Order)
                .MaxAsync();

            return (max ?? 0) + 1;
        }

        private async Task ValidateAsync(int legalDocumentId, TocTargetType targetType, int? startPage, int? endPage, string? anchorId)
        {
            if (targetType == TocTargetType.PageRange)
            {
                if (!startPage.HasValue || startPage.Value <= 0)
                    throw new InvalidOperationException("StartPage is required and must be > 0 for PageRange entries.");

                if (endPage.HasValue && endPage.Value < startPage.Value)
                    throw new InvalidOperationException("EndPage cannot be less than StartPage.");

                // if doc has PageCount, ensure within range
                var pageCount = await _db.LegalDocuments
                    .Where(d => d.Id == legalDocumentId)
                    .Select(d => d.PageCount)
                    .FirstOrDefaultAsync();

                if (pageCount.HasValue)
                {
                    if (startPage.Value > pageCount.Value)
                        throw new InvalidOperationException($"StartPage exceeds document PageCount ({pageCount.Value}).");

                    if (endPage.HasValue && endPage.Value > pageCount.Value)
                        throw new InvalidOperationException($"EndPage exceeds document PageCount ({pageCount.Value}).");
                }
            }
            else // Anchor
            {
                if (string.IsNullOrWhiteSpace(anchorId))
                    throw new InvalidOperationException("AnchorId is required for Anchor entries.");
            }
        }
    }
}
