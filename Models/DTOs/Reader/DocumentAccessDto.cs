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
            public int? RequiredProductId { get; set; }
            public string? RequiredProductName { get; set; }

            public string RequiredAction { get; set; } = "None"; // "Subscribe" | "Buy" | "None"
            public string? CtaLabel { get; set; }
            public string? CtaUrl { get; set; }
            public string? SecondaryCtaLabel { get; set; }
            public string? SecondaryCtaUrl { get; set; }

            public int? PreviewMaxChars { get; set; }
            public int? PreviewMaxParagraphs { get; set; }
            public bool HardStop { get; set; }

            public string GrantSource { get; set; } = "None"; // for debugging/support
            public string? DebugNote { get; set; }


    }
}


