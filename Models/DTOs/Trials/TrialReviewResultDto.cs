namespace LawAfrica.API.Models.DTOs.Trials
{
    public class TrialReviewResultDto
    {
        public bool Ok { get; set; } = true;
        public int RequestId { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
