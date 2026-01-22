namespace LawAfrica.API.Models.Tax.DTOs
{
    public class VatRateDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public decimal RatePercent { get; set; }
        public string? CountryScope { get; set; }
        public bool IsActive { get; set; }
        public DateTime? EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }
    }
}
