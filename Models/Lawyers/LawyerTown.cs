using LawAfrica.API.Models.Locations;

namespace LawAfrica.API.Models.Lawyers
{
    public class LawyerTown
    {
        public int LawyerProfileId { get; set; }
        public LawyerProfile LawyerProfile { get; set; } = null!;

        public int TownId { get; set; }
        public Town Town { get; set; } = null!;

        // optional: distinguish office vs served area
        public bool IsOfficeLocation { get; set; } = false;
    }
}