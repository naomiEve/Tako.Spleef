using Tako.Common.Logging;
using Tako.Common.Numerics;
using Tako.Definitions.Game;
using Tako.Definitions.Game.Players;
using Tako.Definitions.Game.World;
using Tako.Definitions.Network;
using Tako.Definitions.Plugins;
using Tako.Definitions.Plugins.Events;
using Tako.Server.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Numerics;
using System;

namespace Tako.Spleef;

/// <summary>
/// A plugin that adds spleef to the server.
/// </summary>
public class SpleefPlugin : Plugin
{
    /// <summary>
    /// The current state of the spleef game.
    /// </summary>
    private enum SpleefState
    {
        WaitingForPlayers,
        Started,
        End
    }

    /// <summary>
    /// The minimum amount of players.
    /// </summary>
    private const int MIN_PLAYERS = 2;

    /// <summary>
    /// The dimensions in the xz plane.
    /// </summary>
    private const int XZ_DIMENSIONS = 30;

    /// <summary>
    /// The arena height.
    /// </summary>
    private const int HEIGHT = 11;

    /// <inheritdoc/>
    public override string Name => "Spleef";

    /// <summary>
    /// The spleef realm.
    /// </summary>
    private readonly IRealm _spleefRealm;

    /// <summary>
    /// The spleef state.
    /// </summary>
    private SpleefState _state = SpleefState.WaitingForPlayers;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<SpleefPlugin> _logger = LoggerFactory<SpleefPlugin>.Get();

    /// <summary>
    /// Players that are waiting for the next game.
    /// </summary>
    private readonly List<IPlayer> _waitingPlayers = new();

    /// <summary>
    /// The currently playing players.
    /// </summary>
    private readonly List<IPlayer> _players = new();

    /// <summary>
    /// The remove queue.
    /// </summary>
    private readonly Queue<IPlayer> _removeQueue = new();

    /// <summary>
    /// Constructs a new spleef plugin.
    /// </summary>
    /// <param name="server">The server.</param>
    public SpleefPlugin(IServer server)
        : base(server)
    {
        _spleefRealm = Server.RealmManager
            .GetOrCreateRealm("spleef", RealmCreationOptions.None);

        _spleefRealm.OnPlayerJoinedRealm
            .Subscribe(OnPlayerJoinedRealm);

        _spleefRealm.OnPlayerLeftRealm
            .Subscribe(OnPlayerLeftRealm);

        Server.OnServerTick
            .Subscribe(OnSpleefTick);

        BuildSpleefWorld();
    }

    /// <summary>
    /// Builds the world for spleef.
    /// </summary>
    private void BuildSpleefWorld()
    {
        _spleefRealm.GetWorldGenerator()
            .WithDimensions(new Vector3Int(
                XZ_DIMENSIONS, 
                HEIGHT, 
                XZ_DIMENSIONS))
            .WithType(WorldType.Hollow)
            .Build();

        _spleefRealm.World.SpawnPoint = new Vector3(
            XZ_DIMENSIONS / 2, 
            2, 
            XZ_DIMENSIONS / 2);

        BuildTopSpleefLayer();
        BuildSpleefWalls();
    }

    /// <summary>
    /// Builds the spleef walls.
    /// </summary>
    private void BuildSpleefWalls()
    {
        for (var x = 0; x < XZ_DIMENSIONS; x++)
        {
            for (var y = 0; y < HEIGHT; y++)
            {
                _spleefRealm.World!
                    .SetBlock(new Vector3Int(x, y, 0), (byte)ClassicBlockType.BlackRock);

                _spleefRealm.World!
                    .SetBlock(new Vector3Int(0, y, x), (byte)ClassicBlockType.BlackRock);

                _spleefRealm.World!
                    .SetBlock(new Vector3Int(x, y, XZ_DIMENSIONS - 1), (byte)ClassicBlockType.BlackRock);

                _spleefRealm.World!
                    .SetBlock(new Vector3Int(XZ_DIMENSIONS - 1, y, x), (byte)ClassicBlockType.BlackRock);
            }
        }
    }

    /// <summary>
    /// Builds the top spleef layer.
    /// </summary>
    private void BuildTopSpleefLayer()
    {
        for (var x = 0; x < XZ_DIMENSIONS; x++)
        {
            for (var z = 0; z < XZ_DIMENSIONS; z++)
            {
                _spleefRealm.World!
                    .SetBlock(new Vector3Int(x, HEIGHT - 2, z), (byte)ClassicBlockType.Grass);
            }
        }
    }

    /// <summary>
    /// The spleef tick method.
    /// </summary>
    private EventHandlingResult OnSpleefTick(float _)
    {
        if (_state == SpleefState.Started)
        {
            if (_players.Count == 1)
            {
                Server.Chat.SendServerMessageTo(_spleefRealm, $"{_players.First().Name} won!");
                WaitAndSetWaitingForPlayers();
            }
            else if (_players.Count < 1)
            {
                SetSpleefState(SpleefState.WaitingForPlayers);
            }
            else
            {
                foreach (var player in _players)
                {
                    if (player.Position.Y < HEIGHT / 2)
                    {
                        Server.Chat.SendServerMessageTo(_spleefRealm, $"{player.Name} fell!");
                        _removeQueue.Enqueue(player);
                    }
                }

                while (_removeQueue.Count > 0)
                {
                    var player = _removeQueue.Dequeue();
                    _waitingPlayers.Add(player);
                    _players.Remove(player);
                }
            }
        }
        else if (_state == SpleefState.WaitingForPlayers)
        {
            CheckIfEnoughPlayers();
        }

        return EventHandlingResult.Continue;
    }

    /// <summary>
    /// Waits and sets the waiting for players state.
    /// </summary>
    private async void WaitAndSetWaitingForPlayers()
    {
        const int secondsDelay = 10;

        SetSpleefState(SpleefState.End);

        _waitingPlayers.AddRange(_players);
        _players.Clear();

        await Task.Delay(TimeSpan.FromSeconds(secondsDelay));

        SetSpleefState(SpleefState.WaitingForPlayers);
    }

    /// <summary>
    /// Check if we have enough players to start.
    /// </summary>
    private async void CheckIfEnoughPlayers()
    {
        if (_waitingPlayers.Count < MIN_PLAYERS)
            return;

        BuildTopSpleefLayer();

        Server.Chat.SendServerMessageTo(_spleefRealm, "Starting spleef!!");
        _players.AddRange(_waitingPlayers);
        _waitingPlayers.Clear();

        foreach (var player in _players)
        {
            // We go from [1..XZ-2] only so we don't hit the bedrock border.
            // The world in Tako goes from 0..DIM-1, where DIM is the length value in that dimension.
            var x = Random.Shared.Next(1, XZ_DIMENSIONS - 2);
            var z = Random.Shared.Next(1, XZ_DIMENSIONS - 2);

            player.Teleport(new Vector3(x, HEIGHT, z));
        }

        const int gracePeriod = 3;
        await Task.Delay(TimeSpan.FromSeconds(gracePeriod));

        SetSpleefState(SpleefState.Started);
    }

    /// <summary>
    /// Sets the spleef state to something new.
    /// </summary>
    /// <param name="newState">The new state.</param>
    private void SetSpleefState(SpleefState newState)
    {
        _logger.Info($"Setting spleef state to {newState}");
        _state = newState;
    }

    /// <summary>
    /// Called whenever a player joins the spleef realm.
    /// </summary>
    /// <param name="player">The player.</param>
    private EventHandlingResult OnPlayerJoinedRealm(IPlayer player)
    {
        _logger.Info($"{player.Name} joined the spleef realm!");
        _waitingPlayers.Add(player);

        if (_state == SpleefState.WaitingForPlayers)
        {
            Server.Chat.SendServerMessageTo(_spleefRealm, $"{_waitingPlayers.Count}/{MIN_PLAYERS} players needed to start...");
            CheckIfEnoughPlayers();
        }

        return EventHandlingResult.Continue;
    }

    /// <summary>
    /// Called whenever a player joins the spleef realm.
    /// </summary>
    /// <param name="player">The player.</param>
    private EventHandlingResult OnPlayerLeftRealm(IPlayer player)
    {
        _logger.Info($"{player.Name} left the spleef realm!");
        _waitingPlayers.Remove(player);

        if (_state == SpleefState.Started)
            _players.Remove(player);

        return EventHandlingResult.Continue;
    }
}
