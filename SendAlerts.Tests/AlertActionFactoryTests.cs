using SendAlerts.Core.Interfaces;
using SendAlerts.Models;
using SendAlerts.Services;

namespace SendAlerts.Tests;

public class AlertActionFactoryTests
{
    [Fact]
    public void Create_CommandLine_ReturnsAction()
    {
        var config = AlertActionConfig.CreateCommandLine("cmd1", "echo hello");
        var action = AlertActionFactory.Create(config);

        Assert.NotNull(action);
        Assert.Equal("cmd1", action!.InstanceId);
        Assert.Equal(AlertActionType.CommandLine, action.ActionType);
    }

    [Fact]
    public void Create_Telegram_ReturnsAction()
    {
        var config = AlertActionConfig.CreateTelegram("tg1", "token", "chatid");
        var action = AlertActionFactory.Create(config);

        Assert.NotNull(action);
        Assert.Equal(AlertActionType.Telegram, action!.ActionType);
    }

    [Fact]
    public void Create_Discord_ReturnsAction()
    {
        var config = AlertActionConfig.CreateDiscord("dc1", "https://discord.com/api/webhooks/123/abc");
        var action = AlertActionFactory.Create(config);

        Assert.NotNull(action);
        Assert.Equal(AlertActionType.Discord, action!.ActionType);
    }

    [Fact]
    public void Create_InvalidConfig_ReturnsNull()
    {
        var config = AlertActionConfig.CreateCommandLine("cmd1", ""); // empty command = invalid
        Assert.Null(AlertActionFactory.Create(config));
    }

    [Fact]
    public void Create_Null_ReturnsNull()
    {
        Assert.Null(AlertActionFactory.Create(null!));
    }

    [Fact]
    public void Create_Email_ReturnsNull_NotImplemented()
    {
        var config = AlertActionConfig.CreateEmail("em1", "smtp.test.com", 587, "from@t.com", "to@t.com");
        Assert.Null(AlertActionFactory.Create(config));
    }

    [Fact]
    public void Create_DisabledConfig_SetsIsEnabled()
    {
        var config = AlertActionConfig.CreateCommandLine("cmd1", "echo test");
        config.IsEnabled = false;
        var action = AlertActionFactory.Create(config);

        Assert.NotNull(action);
        Assert.False(action!.IsEnabled);
    }

    [Fact]
    public void CreateMany_MixedValidity_ReturnsOnlyValid()
    {
        var configs = new[]
        {
            AlertActionConfig.CreateCommandLine("cmd1", "echo ok"),
            AlertActionConfig.CreateCommandLine("cmd2", ""), // invalid
            AlertActionConfig.CreateTelegram("tg1", "token", "chat"),
        };

        var actions = AlertActionFactory.CreateMany(configs);
        Assert.Equal(2, actions.Length);
    }

    [Fact]
    public void IsImplemented_CorrectResults()
    {
        Assert.True(AlertActionFactory.IsImplemented(AlertActionType.CommandLine));
        Assert.True(AlertActionFactory.IsImplemented(AlertActionType.Telegram));
        Assert.True(AlertActionFactory.IsImplemented(AlertActionType.Discord));
        Assert.False(AlertActionFactory.IsImplemented(AlertActionType.Email));
        Assert.False(AlertActionFactory.IsImplemented(AlertActionType.HttpWebhook));
        Assert.False(AlertActionFactory.IsImplemented(AlertActionType.SystemShutdown));
    }

    [Fact]
    public void GetImplementedTypes_ReturnsThree()
    {
        var types = AlertActionFactory.GetImplementedTypes();
        Assert.Equal(3, types.Length);
        Assert.Contains(AlertActionType.CommandLine, types);
        Assert.Contains(AlertActionType.Telegram, types);
        Assert.Contains(AlertActionType.Discord, types);
    }

    [Fact]
    public void GetDisplayName_ReturnsNonEmpty()
    {
        foreach (AlertActionType type in Enum.GetValues<AlertActionType>())
        {
            Assert.False(string.IsNullOrEmpty(AlertActionFactory.GetDisplayName(type)));
        }
    }
}
