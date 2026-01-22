namespace LawAfrica.API.Models.Tax
{
    public class VatRuleDto
    {
        public int Id { get; set; }
        public string Purpose { get; set; } = "RegistrationFee";
        public string? CountryCode { get; set; } // "KE", "*", null
        public int VatRateId { get; set; }
        public string? VatRateCode { get; set; }
        public decimal? VatRatePercent { get; set; }
        public bool IsActive { get; set; }
        public int Priority { get; set; }
        public DateTime? EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }
    }
}
