using Compass.ViewModels;

namespace Compass.Tests;

public class ChatViewModelTests
{
    [Fact]
    public void ChatMessage_PropertiesSetCorrectly()
    {
        var msg = new ChatMessage
        {
            Sender = "You",
            Text = "Hello",
            IsCode = false
        };

        Assert.Equal("You", msg.Sender);
        Assert.Equal("Hello", msg.Text);
        Assert.False(msg.IsCode);
        Assert.True(msg.IsUser);
        Assert.False(msg.IsSystem);
    }

    [Fact]
    public void ChatMessage_IsUser_TrueForYou()
    {
        var msg = new ChatMessage { Sender = "You" };
        Assert.True(msg.IsUser);
    }

    [Fact]
    public void ChatMessage_IsUser_FalseForAssistant()
    {
        var msg = new ChatMessage { Sender = "Compass" };
        Assert.False(msg.IsUser);
    }

    [Fact]
    public void ChatMessage_IsSystem_TrueForSystem()
    {
        var msg = new ChatMessage { Sender = "System" };
        Assert.True(msg.IsSystem);
    }

    [Fact]
    public void ChatMessage_Timestamp_DefaultsToNow()
    {
        var before = DateTime.Now;
        var msg = new ChatMessage();
        var after = DateTime.Now;

        Assert.InRange(msg.Timestamp, before, after);
    }

    [Fact]
    public void ChatMessage_Images_NullByDefault()
    {
        var msg = new ChatMessage();
        Assert.Null(msg.Images);
        Assert.Null(msg.GeneratedImages);
    }
}
