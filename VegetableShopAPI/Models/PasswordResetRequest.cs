namespace VegetableShopAPI.Models
{
    public class PasswordResetRequest
    {
        public Guid Id { get; set; }
        public int UserId { get; set; }
        public string EncryptedPassword { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? ReviewedAtUtc { get; set; }
        public User User { get; set; } = null!;
    }
}
