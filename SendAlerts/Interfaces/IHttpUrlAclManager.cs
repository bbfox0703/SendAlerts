namespace SendAlerts.Interfaces;

/// <summary>
/// HTTP URL ACL 註冊介面 (平台相依)
/// </summary>
public interface IHttpUrlAclManager
{
    bool TryRegisterUrlAcl(int port);
}
