using SendAlerts.Models;

namespace SendAlerts.Tests;

public class PipeMessageParserTests
{
    [Fact]
    public void Parse_ValidJson_ReturnsSuccess()
    {
        var result = PipeMessageParser.Parse("""{"GroupName":"Critical","CustomMessage":"GPU過熱!"}""");

        Assert.True(result.Success);
        Assert.NotNull(result.Message);
        Assert.Equal("Critical", result.Message!.GroupName);
        Assert.Equal("GPU過熱!", result.Message.CustomMessage);
        Assert.Equal(PipeMessageError.None, result.ErrorType);
    }

    [Fact]
    public void Parse_ValidJson_WithoutCustomMessage_ReturnsSuccess()
    {
        var result = PipeMessageParser.Parse("""{"GroupName":"Default"}""");

        Assert.True(result.Success);
        Assert.Equal("Default", result.Message!.GroupName);
        Assert.Null(result.Message.CustomMessage);
    }

    [Fact]
    public void Parse_CaseInsensitive_ReturnsSuccess()
    {
        var result = PipeMessageParser.Parse("""{"groupname":"Critical","custommessage":"test"}""");

        Assert.True(result.Success);
        Assert.Equal("Critical", result.Message!.GroupName);
        Assert.Equal("test", result.Message.CustomMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyOrNull_ReturnsEmptyMessageError(string? input)
    {
        var result = PipeMessageParser.Parse(input);

        Assert.False(result.Success);
        Assert.Equal(PipeMessageError.EmptyMessage, result.ErrorType);
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsInvalidJsonError()
    {
        var result = PipeMessageParser.Parse("not json");

        Assert.False(result.Success);
        Assert.Equal(PipeMessageError.InvalidJson, result.ErrorType);
    }

    [Fact]
    public void Parse_EmptyGroupName_ReturnsEmptyGroupNameError()
    {
        var result = PipeMessageParser.Parse("""{"GroupName":"","CustomMessage":"test"}""");

        Assert.False(result.Success);
        Assert.Equal(PipeMessageError.EmptyGroupName, result.ErrorType);
    }

    [Fact]
    public void Parse_MissingGroupName_ReturnsEmptyGroupNameError()
    {
        var result = PipeMessageParser.Parse("""{"CustomMessage":"test"}""");

        Assert.False(result.Success);
        Assert.Equal(PipeMessageError.EmptyGroupName, result.ErrorType);
    }

    [Fact]
    public void Parse_TrailingCommas_Accepted()
    {
        var result = PipeMessageParser.Parse("""{"GroupName":"Test",}""");

        Assert.True(result.Success);
        Assert.Equal("Test", result.Message!.GroupName);
    }

    [Fact]
    public void Parse_SetsRawJson()
    {
        var json = """{"GroupName":"Test"}""";
        var result = PipeMessageParser.Parse(json);

        Assert.Equal(json, result.Message!.RawJson);
    }

    [Fact]
    public void ToJson_RoundTrip()
    {
        var original = new PipeMessage { GroupName = "Critical", CustomMessage = "test msg" };
        var json = PipeMessageParser.ToJson(original);
        var result = PipeMessageParser.Parse(json);

        Assert.True(result.Success);
        Assert.Equal("Critical", result.Message!.GroupName);
        Assert.Equal("test msg", result.Message.CustomMessage);
    }
}

public class PipeMessageTests
{
    [Fact]
    public void HasCustomMessage_WithMessage_ReturnsTrue()
    {
        var msg = new PipeMessage { GroupName = "Test", CustomMessage = "hello" };
        Assert.True(msg.HasCustomMessage);
    }

    [Fact]
    public void HasCustomMessage_WithoutMessage_ReturnsFalse()
    {
        var msg = new PipeMessage { GroupName = "Test" };
        Assert.False(msg.HasCustomMessage);
    }

    [Fact]
    public void GetEffectiveMessage_WithCustomMessage_ReturnsCustom()
    {
        var msg = new PipeMessage { GroupName = "Test", CustomMessage = "custom" };
        Assert.Equal("custom", msg.GetEffectiveMessage("template"));
    }

    [Fact]
    public void GetEffectiveMessage_WithoutCustomMessage_ReturnsTemplate()
    {
        var msg = new PipeMessage { GroupName = "Test" };
        Assert.Equal("template", msg.GetEffectiveMessage("template"));
    }

    [Fact]
    public void GetEffectiveMessage_NoCustomNoTemplate_ReturnsFallback()
    {
        var msg = new PipeMessage { GroupName = "MyGroup" };
        Assert.Equal("Alert triggered for group: MyGroup", msg.GetEffectiveMessage());
    }
}
