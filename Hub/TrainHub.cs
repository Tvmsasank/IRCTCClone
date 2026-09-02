using Microsoft.AspNetCore.SignalR;

public class TrainHub : Hub
{
    public async Task JoinTrain(string trainId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"TRAIN_{trainId}");
    }

    public async Task LeaveTrain(string trainId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"TRAIN_{trainId}");
    }
}
