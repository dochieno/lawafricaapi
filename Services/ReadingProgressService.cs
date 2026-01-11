using LawAfrica.API.Data;
using LawAfrica.API.Models;
using Microsoft.EntityFrameworkCore;
using System;

namespace LawAfrica.API.Services
{
    public class ReadingProgressService
    {
        private readonly ApplicationDbContext _db;

        public ReadingProgressService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<LegalDocumentProgress?> GetAsync(int userId, int documentId)
        {
            return await _db.LegalDocumentProgress
                .FirstOrDefaultAsync(p =>
                    p.UserId == userId &&
                    p.LegalDocumentId == documentId);
        }
    }
}
