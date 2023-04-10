namespace sms;

public class UserBroadcastEventArgs : EventArgs
{
    public long UserId { get; set; }
    
    public UserBroadcastEventArgs(long userId)
    {
        UserId = userId;
    }
}

public interface IUserBroadcast
{
    event EventHandler<UserBroadcastEventArgs>? NewMessageSent;
    void NewMessage(long userId);
}

public class UserBroadcast : IUserBroadcast
{
    public event EventHandler<UserBroadcastEventArgs>? NewMessageSent;
    public void NewMessage(long userId)
    {
        NewMessageSent?.Invoke(this, new UserBroadcastEventArgs(userId));
    }
}