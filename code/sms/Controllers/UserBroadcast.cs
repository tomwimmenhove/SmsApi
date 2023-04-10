namespace sms.Controllers;

public class NewMessageSentEventArgs : EventArgs
{
    public long UserId { get; set; }
    
    public NewMessageSentEventArgs(long userId)
    {
        UserId = userId;
    }
}

public class NewSubmitMessageEventArgs : EventArgs
{
    public SendMessageDto Data { get; set; }
    
    public NewSubmitMessageEventArgs(SendMessageDto data)
    {
        Data = data;
    }
}

public interface IBroadcaster
{
    event EventHandler<NewMessageSentEventArgs>? NewMessageSent;
    void NewMessage(long userId);

    event EventHandler<NewSubmitMessageEventArgs>? NewSubmitMessage;
    void SubmitMessage(SendMessageDto data);
}

public class Broadcaster : IBroadcaster
{
    public event EventHandler<NewMessageSentEventArgs>? NewMessageSent;
    public void NewMessage(long userId)
    {
        NewMessageSent?.Invoke(this, new NewMessageSentEventArgs(userId));
    }

    public event EventHandler<NewSubmitMessageEventArgs>? NewSubmitMessage;

    public void SubmitMessage(SendMessageDto data)
    {
        NewSubmitMessage?.Invoke(this, new NewSubmitMessageEventArgs(data));
    }
}