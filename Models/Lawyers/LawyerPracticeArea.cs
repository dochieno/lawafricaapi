namespace LawAfrica.API.Models.Lawyers
{
    public class LawyerPracticeArea
    {
        public int LawyerProfileId { get; set; }
        public LawyerProfile LawyerProfile { get; set; } = null!;

        public int PracticeAreaId { get; set; }
        public PracticeArea PracticeArea { get; set; } = null!;
    }
}