using SendAlerts.Core.Interfaces;
using SendAlerts.Models;
using SendAlerts.Services;

namespace SendAlerts.Tests;

public class AlertServiceTests
{
    private AlertService CreateService()
    {
        return new AlertService();
    }

    #region Action Management

    [Fact]
    public void RegisterAction_Config_RegistersSuccessfully()
    {
        var svc = CreateService();
        var config = AlertActionConfig.CreateCommandLine("cmd1", "echo test");
        svc.RegisterAction(config);

        Assert.Equal(1, svc.ActionCount);
        Assert.NotNull(svc.GetAction("cmd1"));
    }

    [Fact]
    public void RegisterAction_Duplicate_Overwrites()
    {
        var svc = CreateService();
        svc.RegisterAction(AlertActionConfig.CreateCommandLine("cmd1", "echo 1"));
        svc.RegisterAction(AlertActionConfig.CreateCommandLine("cmd1", "echo 2"));

        Assert.Equal(1, svc.ActionCount);
    }

    [Fact]
    public void UnregisterAction_Existing_ReturnsTrue()
    {
        var svc = CreateService();
        svc.RegisterAction(AlertActionConfig.CreateCommandLine("cmd1", "echo test"));

        Assert.True(svc.UnregisterAction("cmd1"));
        Assert.Equal(0, svc.ActionCount);
    }

    [Fact]
    public void UnregisterAction_NonExisting_ReturnsFalse()
    {
        Assert.False(CreateService().UnregisterAction("nope"));
    }

    [Fact]
    public void GetAction_CaseInsensitive()
    {
        var svc = CreateService();
        svc.RegisterAction(AlertActionConfig.CreateCommandLine("Cmd1", "echo test"));

        Assert.NotNull(svc.GetAction("cmd1"));
    }

    [Fact]
    public void GetAllActions_ReturnsAll()
    {
        var svc = CreateService();
        svc.RegisterAction(AlertActionConfig.CreateCommandLine("cmd1", "echo 1"));
        svc.RegisterAction(AlertActionConfig.CreateTelegram("tg1", "tok", "chat"));

        Assert.Equal(2, svc.GetAllActions().Count);
    }

    #endregion

    #region Group Management

    [Fact]
    public void RegisterGroup_Valid_Registers()
    {
        var svc = CreateService();
        svc.RegisterGroup(AlertGroup.CreateDefault());

        Assert.Equal(1, svc.GroupCount);
        Assert.True(svc.GroupExists("Default"));
    }

    [Fact]
    public void RegisterGroup_Invalid_NotRegistered()
    {
        var svc = CreateService();
        svc.RegisterGroup(new AlertGroup { Name = "", MessageTemplate = "{message}" });

        Assert.Equal(0, svc.GroupCount);
    }

    [Fact]
    public void UnregisterGroup_Existing_ReturnsTrue()
    {
        var svc = CreateService();
        svc.RegisterGroup(AlertGroup.CreateDefault());

        Assert.True(svc.UnregisterGroup("Default"));
        Assert.Equal(0, svc.GroupCount);
    }

    [Fact]
    public void GetGroup_CaseInsensitive()
    {
        var svc = CreateService();
        svc.RegisterGroup(AlertGroup.CreateDefault());

        Assert.NotNull(svc.GetGroup("default"));
    }

    [Fact]
    public void InitializeDefaults_CreatesFourGroups()
    {
        var svc = CreateService();
        svc.InitializeDefaults();

        Assert.Equal(4, svc.GroupCount);
        Assert.True(svc.GroupExists("Default"));
        Assert.True(svc.GroupExists("Critical"));
        Assert.True(svc.GroupExists("Warning"));
        Assert.True(svc.GroupExists("Info"));
    }

    #endregion

    #region Execution

    [Fact]
    public async Task ExecuteGroupAsync_NonExistentGroup_ReturnsFailure()
    {
        var svc = CreateService();
        var result = await svc.ExecuteGroupAsync("NoSuchGroup");

        Assert.False(result.Success);
        Assert.Contains("找不到群組", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteGroupAsync_DisabledGroup_ReturnsFailure()
    {
        var svc = CreateService();
        var group = AlertGroup.CreateDefault();
        group.IsEnabled = false;
        svc.RegisterGroup(group);

        var result = await svc.ExecuteGroupAsync("Default");
        Assert.False(result.Success);
        Assert.Contains("已停用", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteGroupAsync_EmptyGroup_Succeeds()
    {
        var svc = CreateService();
        svc.RegisterGroup(AlertGroup.CreateDefault());

        var result = await svc.ExecuteGroupAsync("Default", "test");
        Assert.True(result.Success);
        Assert.Equal(0, result.TotalActions);
    }

    [Fact]
    public async Task ExecuteGroupAsync_MissingAction_RecordsMissing()
    {
        var svc = CreateService();
        var group = AlertGroup.Create("Test", "{message}", "nonexistent-action");
        svc.RegisterGroup(group);

        var result = await svc.ExecuteGroupAsync("Test", "test msg");
        Assert.False(result.Success);
        Assert.Contains("nonexistent-action", result.MissingActions);
    }

    [Fact]
    public async Task ExecuteAsync_NullMessage_ReturnsFailure()
    {
        var svc = CreateService();
        var result = await svc.ExecuteAsync(null!);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ExecuteGroupAsync_FiresAlertExecutedEvent()
    {
        var svc = CreateService();
        svc.RegisterGroup(AlertGroup.CreateDefault());

        AlertExecutedEventArgs? eventArgs = null;
        svc.AlertExecuted += (_, args) => eventArgs = args;

        await svc.ExecuteGroupAsync("Default", "test");

        Assert.NotNull(eventArgs);
        Assert.Equal("Default", eventArgs!.Result.GroupName);
    }

    [Fact]
    public async Task ExecuteGroupAsync_NonExistent_FiresAlertErrorEvent()
    {
        var svc = CreateService();

        AlertErrorEventArgs? errorArgs = null;
        svc.AlertError += (_, args) => errorArgs = args;

        await svc.ExecuteGroupAsync("Missing");

        Assert.NotNull(errorArgs);
        Assert.Equal("Missing", errorArgs!.GroupName);
    }

    #endregion

    #region Clear / LoadFromSettings

    [Fact]
    public void Clear_RemovesEverything()
    {
        var svc = CreateService();
        svc.InitializeDefaults();
        svc.RegisterAction(AlertActionConfig.CreateCommandLine("cmd1", "echo"));

        svc.Clear();

        Assert.Equal(0, svc.ActionCount);
        Assert.Equal(0, svc.GroupCount);
    }

    [Fact]
    public void LoadFromSettings_PopulatesBoth()
    {
        var svc = CreateService();
        var actions = new[] { AlertActionConfig.CreateCommandLine("cmd1", "echo test") };
        var groups = new[] { AlertGroup.CreateDefault() };

        svc.LoadFromSettings(actions, groups);

        Assert.Equal(1, svc.ActionCount);
        Assert.Equal(1, svc.GroupCount);
    }

    #endregion
}
