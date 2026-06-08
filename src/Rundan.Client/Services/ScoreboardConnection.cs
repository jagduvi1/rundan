using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Rundan.Shared.Contracts;
using Rundan.Shared.Realtime;

namespace Rundan.Client.Services;

/// <summary>
/// Wraps the in-process SignalR hub connection for one activity's live scoreboard.
/// Auto-reconnects (mobile signal drops) and re-joins the activity group on reconnect.
/// </summary>
public sealed class ScoreboardConnection(NavigationManager nav, AppState state) : IAsyncDisposable
{
    private HubConnection? _connection;
    private readonly List<int> _activityIds = new();

    public event Action<ScoreboardDto>? ScoreboardUpdated;
    public event Action<ParticipantDto>? ParticipantJoined;
    public event Action<ActivityStatusChangedDto>? StatusChanged;
    public event Action? ConnectionStateChanged;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;
    public bool IsReconnecting => _connection?.State == HubConnectionState.Reconnecting;

    public Task StartAsync(int activityId) => StartAsync(new[] { activityId });

    /// <summary>Connects and subscribes to live updates for one or more activities.</summary>
    public async Task StartAsync(IEnumerable<int> activityIds)
    {
        _activityIds.Clear();
        _activityIds.AddRange(activityIds.Distinct());

        _connection = new HubConnectionBuilder()
            .WithUrl(nav.ToAbsoluteUri(HubRoutes.Scoreboard), opts =>
            {
                // Browsers can't add headers to the WebSocket handshake, so SignalR sends
                // the access code as the access_token query string (read by the gate middleware).
                opts.AccessTokenProvider = () => Task.FromResult(state.AccessCode);
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On<ScoreboardDto>(ScoreboardMessages.ScoreboardUpdated, d => ScoreboardUpdated?.Invoke(d));
        _connection.On<ParticipantDto>(ScoreboardMessages.ParticipantJoined, p => ParticipantJoined?.Invoke(p));
        _connection.On<ActivityStatusChangedDto>(ScoreboardMessages.ActivityStatusChanged,
            s => StatusChanged?.Invoke(s));

        _connection.Reconnecting += _ => { ConnectionStateChanged?.Invoke(); return Task.CompletedTask; };
        _connection.Reconnected += async _ =>
        {
            // SignalR groups are NOT preserved across a reconnect, so we must re-join.
            // Guard against a teardown race and a transient invoke failure; the consumer
            // also refetches on reconnect, so a missed message can't leave the board stale.
            var connection = _connection;
            if (connection is not null)
            {
                try
                {
                    foreach (var id in _activityIds)
                    {
                        await connection.InvokeAsync(ScoreboardHubMethods.JoinActivity, id);
                    }
                }
                catch
                {
                    /* retried on the next reconnect; consumer refetch covers the gap */
                }
            }

            ConnectionStateChanged?.Invoke();
        };
        _connection.Closed += _ => { ConnectionStateChanged?.Invoke(); return Task.CompletedTask; };

        await _connection.StartAsync();
        foreach (var id in _activityIds)
        {
            await _connection.InvokeAsync(ScoreboardHubMethods.JoinActivity, id);
        }

        ConnectionStateChanged?.Invoke();
    }

    /// <summary>Subscribes to an additional activity at runtime (e.g. the host opened the next one).</summary>
    public async Task EnsureJoinedAsync(int activityId)
    {
        if (_activityIds.Contains(activityId))
        {
            return;
        }

        _activityIds.Add(activityId);
        if (_connection is { State: HubConnectionState.Connected } connection)
        {
            try
            {
                await connection.InvokeAsync(ScoreboardHubMethods.JoinActivity, activityId);
            }
            catch
            {
                /* will be re-joined on next reconnect */
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        var connection = _connection;
        _connection = null; // so a racing reconnect handler short-circuits
        if (connection is not null)
        {
            try
            {
                await connection.DisposeAsync();
            }
            catch
            {
                /* best effort on teardown */
            }
        }
    }
}
