using SendAlerts.Core.Interfaces;
using SendAlerts.Models;

namespace SendAlerts.Tests;

public class AlertActionConfigTests
{
    [Fact]
    public void Validate_EmptyInstanceId_ReturnsInvalid()
    {
        var config = new AlertActionConfig { InstanceId = "", ActionType = AlertActionType.CommandLine, Command = "echo" };
        Assert.False(config.Validate().IsValid);
    }

    [Fact]
    public void Validate_CommandLine_Valid()
    {
        var config = AlertActionConfig.CreateCommandLine("cmd1", "echo hello");
        Assert.True(config.Validate().IsValid);
    }

    [Fact]
    public void Validate_CommandLine_EmptyCommand_Invalid()
    {
        var config = AlertActionConfig.CreateCommandLine("cmd1", "");
        Assert.False(config.Validate().IsValid);
    }

    [Fact]
    public void Validate_Telegram_Valid()
    {
        var config = AlertActionConfig.CreateTelegram("tg1", "token123", "chat456");
        Assert.True(config.Validate().IsValid);
    }

    [Fact]
    public void Validate_Telegram_MissingToken_Invalid()
    {
        var config = AlertActionConfig.CreateTelegram("tg1", "", "chat456");
        Assert.False(config.Validate().IsValid);
    }

    [Fact]
    public void Validate_Telegram_MissingChatId_Invalid()
    {
        var config = AlertActionConfig.CreateTelegram("tg1", "token", "");
        Assert.False(config.Validate().IsValid);
    }

    [Fact]
    public void Validate_Discord_Valid()
    {
        var config = AlertActionConfig.CreateDiscord("dc1", "https://discord.com/api/webhooks/123/abc");
        Assert.True(config.Validate().IsValid);
    }

    [Fact]
    public void Validate_Discord_InvalidUrl_Invalid()
    {
        var config = AlertActionConfig.CreateDiscord("dc1", "not-a-url");
        Assert.False(config.Validate().IsValid);
    }

    [Fact]
    public void Validate_Discord_EmptyUrl_Invalid()
    {
        var config = AlertActionConfig.CreateDiscord("dc1", "");
        Assert.False(config.Validate().IsValid);
    }

    [Fact]
    public void Validate_Email_Valid()
    {
        var config = AlertActionConfig.CreateEmail("em1", "smtp.test.com", 587, "from@test.com", "to@test.com");
        Assert.True(config.Validate().IsValid);
    }

    [Fact]
    public void Validate_Email_MissingHost_Invalid()
    {
        var config = AlertActionConfig.CreateEmail("em1", "", 587, "from@test.com", "to@test.com");
        Assert.False(config.Validate().IsValid);
    }

    [Fact]
    public void Validate_Email_InvalidPort_Invalid()
    {
        var config = AlertActionConfig.CreateEmail("em1", "smtp.test.com", 0, "from@test.com", "to@test.com");
        Assert.False(config.Validate().IsValid);
    }

    [Fact]
    public void Validate_HttpWebhook_Valid()
    {
        var config = AlertActionConfig.CreateHttpWebhook("wh1", "https://hooks.example.com/alert");
        Assert.True(config.Validate().IsValid);
    }

    [Fact]
    public void Validate_HttpWebhook_InvalidUrl_Invalid()
    {
        var config = AlertActionConfig.CreateHttpWebhook("wh1", "ftp://invalid");
        Assert.False(config.Validate().IsValid);
    }

    [Fact]
    public void Validate_SystemShutdown_AlwaysValid()
    {
        var config = AlertActionConfig.CreateSystemShutdown("sd1");
        Assert.True(config.Validate().IsValid);
    }

    [Fact]
    public void Factory_CommandLine_SetsDefaults()
    {
        var config = AlertActionConfig.CreateCommandLine("cmd1", "echo test", 60);
        Assert.Equal(AlertActionType.CommandLine, config.ActionType);
        Assert.Equal(60, config.CooldownSeconds);
        Assert.True(config.IsEnabled);
    }

    [Fact]
    public void Factory_Discord_SetsUsername()
    {
        var config = AlertActionConfig.CreateDiscord("dc1", "https://discord.com/webhook", "AlertBot");
        Assert.Equal("AlertBot", config.DiscordUsername);
    }
}
