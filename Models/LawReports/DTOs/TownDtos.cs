using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.DTOs.Locations
{
    public class TownDto
    {
        public int Id { get; set; }
        public int CountryId { get; set; }
        public string PostCode { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public class TownUpsertDto
    {
        [Range(1, int.MaxValue)]
        public int CountryId { get; set; }

        [Required, MaxLength(20)]
        public string PostCode { get; set; } = "";

        [Required, MaxLength(120)]
        public string Name { get; set; } = "";
    }
}
