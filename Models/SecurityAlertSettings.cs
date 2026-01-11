public class SecurityAlertSettings
{
    public bool EnableEmailAlerts { get; set; }
    public int FailedLoginThreshold { get; set; }
    public bool LockoutEmailToUser { get; set; }
    public string AdminAlertEmail { get; set; } = string.Empty;
}
