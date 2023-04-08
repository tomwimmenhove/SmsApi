namespace sms;

public class UserBroadcastEventArgs : EventArgs
{
    public uint UserId { get; set; }
    
    public UserBroadcastEventArgs(uint userId)
    {
        UserId = userId;
    }
}

public interface IUserBroadcast
{
    event EventHandler<UserBroadcastEventArgs>? NewMessageSent;
    void NewMessage(uint userId);
}

public class UserBroadcast : IUserBroadcast
{
    public event EventHandler<UserBroadcastEventArgs>? NewMessageSent;
    public void NewMessage(uint userId)
    {
        NewMessageSent?.Invoke(this, new UserBroadcastEventArgs(userId));
    }
}