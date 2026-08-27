using Microsoft.AspNetCore.SignalR;

namespace App.Api.Hubs;

public class AuctionHub : Hub
{
    public async Task JoinAuction(int auctionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(auctionId));
    }

    public async Task LeaveAuction(int auctionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(auctionId));
    }

    public static string GroupName(int auctionId) => $"auction-{auctionId}";
}

public interface IAuctionBroadcaster
{
    Task BroadcastAsync(int auctionId, string eventType, object payload);
}

public class AuctionBroadcaster : IAuctionBroadcaster
{
    private readonly IHubContext<AuctionHub> _hub;
    public AuctionBroadcaster(IHubContext<AuctionHub> hub) { _hub = hub; }

    public async Task BroadcastAsync(int auctionId, string eventType, object payload)
    {
        await _hub.Clients.Group(AuctionHub.GroupName(auctionId)).SendAsync(eventType, payload);
    }
}
