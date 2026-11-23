using System.Text.Encodings.Web;

namespace VegetableShopAPI.Services;

public class EmployeeNotificationService
{
    private readonly IEmailService _emailService;
    private readonly ILogger<EmployeeNotificationService> _logger;

    public EmployeeNotificationService(
        IEmailService emailService,
        ILogger<EmployeeNotificationService> logger)
    {
        _emailService = emailService;
        _logger = logger;
    }

    public Task<bool> SendEmployeeAddedAsync(
        string fullName,
        string email,
        string employeeCode,
        string position)
    {
        var name = HtmlEncoder.Default.Encode(fullName);
        var code = HtmlEncoder.Default.Encode(employeeCode);
        var encodedPosition = HtmlEncoder.Default.Encode(position);
        var body = $"""
            <div style="font-family: Arial, Helvetica, sans-serif; color: #374151; line-height: 1.7;">
                <h2 style="color: #16834a;">Thông báo nhân sự - VEGETABLE</h2>
                <p>Xin chào {name},</p>
                <p>Bạn đã được thêm vào đội ngũ nhân viên của cửa hàng VEGETABLE.</p>
                <p><strong>Mã nhân viên:</strong> {code}<br />
                <strong>Chức vụ:</strong> {encodedPosition}</p>
                <p>Vui lòng liên hệ quản lý cửa hàng nếu bạn cần hỗ trợ về tài khoản hoặc công việc.</p>
                <p>Thân mến,<br /><strong>Đội ngũ VEGETABLE</strong></p>
            </div>
            """;

        return TrySendAsync(
            email,
            "Thông báo bạn đã trở thành nhân viên VEGETABLE",
            body);
    }

    public Task<bool> SendAccountChangedAsync(
        string fullName,
        string email,
        string? employeeCode,
        string? previousRole,
        string? newRole,
        bool? accountIsActive,
        bool? employeeIsActive,
        IReadOnlyCollection<string>? permissionChanges = null)
    {
        var name = HtmlEncoder.Default.Encode(fullName);
        var changes = new List<string>();

        if (previousRole != null &&
            newRole != null &&
            previousRole != newRole)
        {
            changes.Add(
                $"Vai trò của bạn đã thay đổi từ {RoleLabel(previousRole)} sang {RoleLabel(newRole)}.");
        }

        if (accountIsActive == false)
            changes.Add("Tài khoản đăng nhập của bạn đã bị vô hiệu hóa.");
        else if (accountIsActive == true)
            changes.Add("Tài khoản đăng nhập của bạn đã được kích hoạt.");

        if (employeeIsActive == false)
            changes.Add("Hồ sơ nhân viên của bạn đã bị vô hiệu hóa.");
        else if (employeeIsActive == true)
            changes.Add("Hồ sơ nhân viên của bạn đã được kích hoạt.");

        if (permissionChanges != null)
            changes.AddRange(permissionChanges);

        var changeList = string.Join(
            Environment.NewLine,
            changes.Select(change => $"<li>{HtmlEncoder.Default.Encode(change)}</li>"));
        var codeLine = string.IsNullOrWhiteSpace(employeeCode)
            ? string.Empty
            : $"<p><strong>Mã nhân viên:</strong> {HtmlEncoder.Default.Encode(employeeCode)}</p>";
        var body = $"""
            <div style="font-family: Arial, Helvetica, sans-serif; color: #374151; line-height: 1.7;">
                <h2 style="color: #16834a;">Thông báo cập nhật tài khoản - VEGETABLE</h2>
                <p>Xin chào {name},</p>
                <p>Thông tin quyền hoặc trạng thái tài khoản của bạn vừa được cập nhật:</p>
                <ul>{changeList}</ul>
                {codeLine}
                <p>Nếu bạn cần hỗ trợ, vui lòng liên hệ quản lý cửa hàng.</p>
                <p>Thân mến,<br /><strong>Đội ngũ VEGETABLE</strong></p>
            </div>
            """;

        return TrySendAsync(
            email,
            "Thông báo cập nhật quyền hoặc trạng thái tài khoản",
            body);
    }

    private async Task<bool> TrySendAsync(
        string email,
        string subject,
        string body)
    {
        try
        {
            await _emailService.SendEmailAsync(email, subject, body);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Không gửi được email thông báo nhân viên tới {Email}.",
                email);
            return false;
        }
    }

    private static string RoleLabel(string role) => role switch
    {
        "Admin" => "Quản trị viên",
        "AdminVIP" => "Quản trị viên VIP",
        "Staff" => "Nhân viên",
        "Customer" => "Khách hàng",
        _ => role
    };
}
