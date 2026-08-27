// Shared SignalR connection helper.
// On connect/reconnect, callers MUST re-fetch full auction state via REST
// before trusting further push events (see live-auction.js / display.js).
function createAuctionConnection(auctionId, handlers, onReconnectedOrConnected) {
    const normalizedAuctionId = Number(auctionId);
    if (!Number.isInteger(normalizedAuctionId) || normalizedAuctionId <= 0) {
        throw new Error('A valid numeric auction id is required for real-time updates');
    }

    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/auction', { accessTokenFactory: () => Api.token() || '' })
        .withAutomaticReconnect()
        .build();

    Object.entries(handlers || {}).forEach(([event, fn]) => connection.on(event, fn));

    connection.onreconnected(() => {
        connection.invoke('JoinAuction', normalizedAuctionId).catch(console.error);
        if (onReconnectedOrConnected) onReconnectedOrConnected();
    });

    connection.start()
        .then(() => connection.invoke('JoinAuction', normalizedAuctionId))
        .then(() => { if (onReconnectedOrConnected) onReconnectedOrConnected(); })
        .catch(console.error);

    return connection;
}
