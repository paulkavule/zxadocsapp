
namespace zxadocsfe.Dtos;

public record UserData
{
    public string OrgId { get; set; } = string.Empty;
    public string UserReference { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime LoginDate { set; get; }
    public string RoleName { get; set; } = string.Empty;
    public string RoleId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
}