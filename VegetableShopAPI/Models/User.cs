namespace VegetableShopAPI.Models
{
    public class User
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public DateOnly? DateOfBirth { get; set; }
        public string Password { get; set; } = string.Empty;
        public string Role { get; set; } = "Customer"; // Phân quyền: Customer hoặc Admin

    }
}