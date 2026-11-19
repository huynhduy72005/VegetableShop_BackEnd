namespace VegetableShopAPI.Models
{
    public class PasswordResetOtp
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string CodeHash { get; set; } = string.Empty;
        public string Salt { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? UsedAtUtc { get; set; }
        public int FailedAttempts { get; set; }
        public User User { get; set; } = null!;
    }
}
