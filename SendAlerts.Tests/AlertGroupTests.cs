using SendAlerts.Models;

namespace SendAlerts.Tests;

public class AlertGroupTests
{
    [Fact]
    public void FormatMessage_ReplacesAllVariables()
    {
        var group = new AlertGroup
        {
            Name = "Critical",
            MessageTemplate = "[{group_name}] {message} at {date} {time}"
        };

        var result = group.FormatMessage("GPU overheating");

        Assert.Contains("[Critical]", result);
        Assert.Contains("GPU overheating", result);
        Assert.Contains(DateTime.Now.ToString("yyyy-MM-dd"), result);
    }

    [Fact]
    public void FormatMessage_NullCustomMessage_UsesEmpty()
    {
        var group = new AlertGroup
        {
            Name = "Test",
            MessageTemplate = "Alert: {message}"
        };

        var result = group.FormatMessage(null);
        Assert.Equal("Alert: ", result);
    }

    [Fact]
    public void GetEffectiveMessage_WithCustomAndDefaultTemplate_ReturnsCustomDirectly()
    {
        var group = new AlertGroup
        {
            Name = "Test",
            MessageTemplate = "{message}" // default template
        };

        Assert.Equal("custom msg", group.GetEffectiveMessage("custom msg"));
    }

    [Fact]
    public void GetEffectiveMessage_WithCustomAndCustomTemplate_FormatsWithTemplate()
    {
        var group = new AlertGroup
        {
            Name = "Critical",
            MessageTemplate = "[ALERT] {message} ({group_name})"
        };

        var result = group.GetEffectiveMessage("GPU hot");
        Assert.Contains("[ALERT] GPU hot (Critical)", result);
    }

    [Fact]
    public void GetEffectiveMessage_NullCustom_UseTemplateWhenEmpty_True()
    {
        var group = new AlertGroup
        {
            Name = "Test",
            MessageTemplate = "Alert from {group_name}"
        };

        var result = group.GetEffectiveMessage(null, useTemplateWhenCustomEmpty: true);
        Assert.Contains("Alert from Test", result);
    }

    [Fact]
    public void GetEffectiveMessage_NullCustom_UseTemplateWhenEmpty_False()
    {
        var group = new AlertGroup { Name = "Test", MessageTemplate = "Alert: {message}" };
        Assert.Equal(string.Empty, group.GetEffectiveMessage(null, useTemplateWhenCustomEmpty: false));
    }

    [Theory]
    [InlineData("Valid", true)]
    [InlineData("Valid-Name_123", true)]
    [InlineData("a", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("123abc", false)]
    [InlineData("-invalid", false)]
    [InlineData("has space", false)]
    [InlineData("特殊字元", false)]
    public void IsValidName_VariousInputs(string? name, bool expected)
    {
        Assert.Equal(expected, AlertGroup.IsValidName(name));
    }

    [Fact]
    public void Validate_ValidGroup_ReturnsValid()
    {
        var group = AlertGroup.CreateDefault();
        Assert.True(group.Validate().IsValid);
    }

    [Fact]
    public void Validate_EmptyName_ReturnsInvalid()
    {
        var group = new AlertGroup { Name = "", MessageTemplate = "{message}" };
        Assert.False(group.Validate().IsValid);
    }

    [Fact]
    public void Validate_EmptyTemplate_ReturnsInvalid()
    {
        var group = new AlertGroup { Name = "Test", MessageTemplate = "" };
        Assert.False(group.Validate().IsValid);
    }

    [Fact]
    public void ValidateWithActions_MissingAction_ReturnsInvalid()
    {
        var group = AlertGroup.Create("Test", "{message}", "action1", "action2");
        var result = group.ValidateWithActions(new[] { "action1" });

        Assert.False(result.IsValid);
        Assert.Contains("action2", result.ErrorMessage!);
    }

    [Fact]
    public void ValidateWithActions_AllPresent_ReturnsValid()
    {
        var group = AlertGroup.Create("Test", "{message}", "a1", "a2");
        Assert.True(group.ValidateWithActions(new[] { "a1", "a2", "a3" }).IsValid);
    }

    [Fact]
    public void AddAction_Duplicate_NotAdded()
    {
        var group = AlertGroup.CreateDefault();
        group.AddAction("action1");
        group.AddAction("action1");

        Assert.Single(group.ActionInstanceIds);
    }

    [Fact]
    public void AddAction_CaseInsensitive_NotDuplicated()
    {
        var group = AlertGroup.CreateDefault();
        group.AddAction("Action1");
        group.AddAction("action1");

        Assert.Single(group.ActionInstanceIds);
    }

    [Fact]
    public void RemoveAction_Existing_ReturnsTrue()
    {
        var group = AlertGroup.Create("Test", "{message}", "a1", "a2");
        Assert.True(group.RemoveAction("a1"));
        Assert.Single(group.ActionInstanceIds);
    }

    [Fact]
    public void RemoveAction_NonExisting_ReturnsFalse()
    {
        var group = AlertGroup.CreateDefault();
        Assert.False(group.RemoveAction("nonexistent"));
    }

    [Fact]
    public void ContainsAction_CaseInsensitive()
    {
        var group = AlertGroup.Create("Test", "{message}", "Action1");
        Assert.True(group.ContainsAction("action1"));
    }

    [Fact]
    public void FactoryMethods_CreateValidGroups()
    {
        Assert.True(AlertGroup.CreateDefault().Validate().IsValid);
        Assert.True(AlertGroup.CreateCritical("a1").Validate().IsValid);
        Assert.True(AlertGroup.CreateWarning().Validate().IsValid);
        Assert.True(AlertGroup.CreateInfo("a1", "a2").Validate().IsValid);
    }
}
