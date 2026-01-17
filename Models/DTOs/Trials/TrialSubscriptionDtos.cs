using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.DTOs.Admin.Trials
{
    public enum DurationUnit
    {
        Days = 1,
        Months = 2
    }

    public class GrantTrialRequest : IValidatableObject
    {
        [Required]
        public int UserId { get; set; }

        [Required]
        public int ContentProductId { get; set; }

        [Required]
        public DurationUnit Unit { get; set; }

        /// <summary>
        /// Number of days/months to grant. Example:
        /// - Unit=Days, Value=7
        /// - Unit=Months, Value=1
        /// </summary>
        [Range(1, 365)]
        public int Value { get; set; }

        /// <summary>
        /// If true, when a trial already exists and is active, we EXTEND it.
        /// If false, we reset the trial window from now.
        /// Default is extend.
        /// </summary>
        public bool ExtendIfActive { get; set; } = true;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Unit == DurationUnit.Months && Value > 24)
                yield return new ValidationResult("Months value is too large. Use <= 24.", new[] { nameof(Value) });
        }
    }

    public class ExtendTrialRequest : IValidatableObject
    {
        [Required]
        public int UserId { get; set; }

        [Required]
        public int ContentProductId { get; set; }

        [Required]
        public DurationUnit Unit { get; set; }

        [Range(1, 365)]
        public int Value { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Unit == DurationUnit.Months && Value > 24)
                yield return new ValidationResult("Months value is too large. Use <= 24.", new[] { nameof(Value) });
        }
    }

    public class RevokeTrialRequest
    {
        [Required]
        public int UserId { get; set; }

        [Required]
        public int ContentProductId { get; set; }
    }

    /// <summary>
    /// Optional: admin grant paid subscription using the same record type.
    /// IsTrial=false.
    /// </summary>
    public class GrantPaidSubscriptionRequest : IValidatableObject
    {
        [Required]
        public int UserId { get; set; }

        [Required]
        public int ContentProductId { get; set; }

        /// <summary>
        /// Paid durations: 1, 6, 12 months
        /// </summary>
        [Required]
        public int Months { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Months != 1 && Months != 6 && Months != 12)
                yield return new ValidationResult("Paid subscription months must be 1, 6, or 12.", new[] { nameof(Months) });
        }
    }

    public class TrialSubscriptionResultDto
    {
        public int SubscriptionId { get; set; }
        public int UserId { get; set; }
        public int ContentProductId { get; set; }

        public bool IsTrial { get; set; }
        public string Status { get; set; } = "";

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public int? GrantedByUserId { get; set; }
        public string Action { get; set; } = "";
    }
}
