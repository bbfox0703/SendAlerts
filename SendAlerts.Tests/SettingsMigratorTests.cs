using SendAlerts.Services;

namespace SendAlerts.Tests;

public class SettingsMigratorTests
{
    [Fact]
    public void Migrate_NullSettings_ReturnsFalse()
    {
        Assert.False(SettingsMigrator.Migrate(null!));
    }

    [Fact]
    public void Migrate_NoLegacySettings_ReturnsFalse()
    {
        var settings = new AppSettings { SettingsVersion = 1 };
        Assert.False(SettingsMigrator.Migrate(settings));
    }

    [Fact]
    public void Migrate_WithCommandLine_CreatesAction()
    {
        var settings = new AppSettings
        {
            SettingsVersion = 1,
            CommandLineAlertEnabled = true,
            CommandLineAlertCommand = "echo alert",
            CommandLineAlertCooldownSeconds = 60
        };

        Assert.True(SettingsMigrator.Migrate(settings));
        Assert.Equal(2, settings.SettingsVersion);
        Assert.Single(settings.AlertActions);
        Assert.Equal("CommandLine_Legacy", settings.AlertActions[0].InstanceId);
        Assert.Equal(60, settings.AlertActions[0].CooldownSeconds);
    }

    [Fact]
    public void Migrate_WithTelegram_CreatesAction()
    {
        var settings = new AppSettings
        {
            SettingsVersion = 1,
            TelegramAlertEnabled = true,
            TelegramBotToken = "bot-token",
            TelegramChatId = "chat-id"
        };

        Assert.True(SettingsMigrator.Migrate(settings));
        Assert.Single(settings.AlertActions);
        Assert.Equal("Telegram_Legacy", settings.AlertActions[0].InstanceId);
    }

    [Fact]
    public void Migrate_WithBoth_CreatesDefaultAndCriticalGroups()
    {
        var settings = new AppSettings
        {
            SettingsVersion = 1,
            CommandLineAlertEnabled = true,
            CommandLineAlertCommand = "echo test",
            TelegramAlertEnabled = true,
            TelegramBotToken = "tok",
            TelegramChatId = "chat"
        };

        SettingsMigrator.Migrate(settings);

        Assert.Equal(2, settings.AlertActions.Count);
        Assert.Equal(2, settings.AlertGroups.Count); // Default + Critical
        Assert.True(settings.UseAlertCenterMode);
    }

    [Fact]
    public void Migrate_LineNotify_DisabledNotMigrated()
    {
        var settings = new AppSettings
        {
            SettingsVersion = 1,
            LineNotifyAlertEnabled = true,
            LineNotifyAccessToken = "token",
            // Need at least one other alert to trigger migration
            CommandLineAlertEnabled = true,
            CommandLineAlertCommand = "echo"
        };

        SettingsMigrator.Migrate(settings);
        Assert.False(settings.LineNotifyAlertEnabled);
        // Only CommandLine should be migrated, no LINE action
        Assert.Single(settings.AlertActions);
    }

    [Fact]
    public void Migrate_AlreadyV2_NoChange()
    {
        var settings = new AppSettings
        {
            SettingsVersion = 2,
            CommandLineAlertEnabled = true,
            CommandLineAlertCommand = "echo test"
        };

        Assert.False(SettingsMigrator.Migrate(settings));
    }

    [Fact]
    public void NeedsMigration_V1WithLegacy_ReturnsTrue()
    {
        var settings = new AppSettings
        {
            SettingsVersion = 1,
            CommandLineAlertEnabled = true,
            CommandLineAlertCommand = "echo"
        };

        Assert.True(SettingsMigrator.NeedsMigration(settings));
    }

    [Fact]
    public void NeedsMigration_V2_ReturnsFalse()
    {
        var settings = new AppSettings { SettingsVersion = 2 };
        Assert.False(SettingsMigrator.NeedsMigration(settings));
    }

    [Fact]
    public void NeedsDefaultGroups_EmptyGroups_NoLegacy_ReturnsTrue()
    {
        var settings = new AppSettings();
        Assert.True(SettingsMigrator.NeedsDefaultGroups(settings));
    }

    [Fact]
    public void NeedsDefaultGroups_HasGroups_ReturnsFalse()
    {
        var settings = new AppSettings();
        settings.AlertGroups.Add(Models.AlertGroup.CreateDefault());
        Assert.False(SettingsMigrator.NeedsDefaultGroups(settings));
    }

    [Fact]
    public void InitializeDefaultGroups_CreatesForGroups()
    {
        var settings = new AppSettings();
        Assert.True(SettingsMigrator.InitializeDefaultGroups(settings));
        Assert.Equal(4, settings.AlertGroups.Count);
        Assert.True(settings.UseAlertCenterMode);
        Assert.Equal(SettingsMigrator.CurrentVersion, settings.SettingsVersion);
    }

    [Fact]
    public void InitializeDefaultGroups_HasLegacy_ReturnsFalse()
    {
        var settings = new AppSettings
        {
            CommandLineAlertEnabled = true,
            CommandLineAlertCommand = "echo"
        };

        Assert.False(SettingsMigrator.InitializeDefaultGroups(settings));
    }

    [Fact]
    public void InitializeDefaultGroups_AlreadyHasGroups_ReturnsFalse()
    {
        var settings = new AppSettings();
        settings.AlertGroups.Add(Models.AlertGroup.CreateDefault());

        Assert.False(SettingsMigrator.InitializeDefaultGroups(settings));
    }
}
