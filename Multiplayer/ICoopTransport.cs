using System;

public interface ICoopTransport : IDisposable
{
    int BoundPort { get; }
    bool TryPoll(out CoopTransport.Event item);
    bool Send(int peer, string text);
    void Disconnect(int peer);
    void DisconnectAfterSend(int peer);
}
