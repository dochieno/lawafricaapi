namespace LawAfrica.API.Models.DTOs.Reader
{
        public class DocumentAccessDto
        {
            public int DocumentId { get; set; }
            public bool IsPremium { get; set; }
            public bool HasFullAccess { get; set; }
            public int PreviewMaxPages { get; set; }  // e.g. 20
            public string Message { get; set; } = "";
            public bool IsBlocked { get; set; } = false;
            public string? BlockReason { get; set; }
            public string? BlockMessage { get; set; }
            public bool CanPurchaseIndividually { get; set; }
            public string? PurchaseDisabledReason { get; set; }     

    }
}


