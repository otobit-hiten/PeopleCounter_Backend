using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace PeopleCounter_Backend.Services
{
    [Authorize]
    public class PeopleCounterHub : Hub
    {
        public Task JoinDashboard()
            => Groups.AddToGroupAsync(Context.ConnectionId, "dashboard");

        public Task JoinBuilding(string building)
            => Groups.AddToGroupAsync(Context.ConnectionId, $"building:{building}");

        public Task LeaveBuilding(string building)
            => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"building:{building}");
    }

}
