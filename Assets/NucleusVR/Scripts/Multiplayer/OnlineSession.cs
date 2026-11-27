using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace NucleusVR.Multiplayer
{
    /// <summary>Coordinates Unity Lobby membership and a Relay transport for one online room.</summary>
    [DisallowMultipleComponent]
    public sealed class OnlineSession : MonoBehaviour
    {
        private const string ProtocolField = "nucleusBuild";
        private const string RelayField = "relayAccess";
        private const string RelayProtocol = "dtls";
        private const double MaintenanceInterval = 12;
        private const double MaintenanceRetry = 4;
        private const double ConnectTimeout = 16;
        private const string IdleStatus = "Online services idle";

        [SerializeField] private DirectSession transport;

        private CancellationTokenSource operation;
        private Task maintenance;
        private Lobby room;
        private string localPlayerId;
        private string roomCode = "";
        private bool ownsTransport;
        private bool hasConnected;
        private int maintenanceFailures;
        private double nextMaintenance;

        public string Status { get; private set; } = IdleStatus;
        public string RoomCode => InRoom ? roomCode : "";
        public bool Busy => operation != null;
        public bool InRoom => hasConnected && HasRelayTransport && room != null;

        private bool HasRelayTransport => ownsTransport && transport != null && transport.IsSessionActive;
        private bool IsRoomHost => room != null && room.HostId == localPlayerId;
        private string CompatibleBuild =>
            "nucleus/" + transport.ProtocolVersion.ToString(CultureInfo.InvariantCulture) + "/" + Application.version;

        public void Configure(DirectSession session)
        {
            transport = session;
        }

        public void QuickJoin()
        {
            Begin(QuickJoinAsync);
        }

        public void CreatePrivate()
        {
            Begin(token => CreateAsync(true, token));
        }

        public void JoinCode(string input)
        {
            if (Busy || room != null)
                return;

            if (!OnlineRoomCode.TryNormalize(input, out string code))
            {
                Status = "Enter a six-character room code.";
                return;
            }

            Begin(token => JoinByCodeAsync(code, token));
        }

        public void Leave()
        {
            if (operation != null)
            {
                operation.Cancel();
                StopTransport();
                Status = "Leaving online room…";
                return;
            }

            if (room != null || ownsTransport)
                StartClose(IdleStatus);
        }

        private void OnDisable()
        {
            Leave();
        }

        private void Update()
        {
            if (!hasConnected || Busy)
                return;

            if (!HasRelayTransport || transport.State is SessionState.Failed or SessionState.Disconnecting)
            {
                StartClose("Online connection closed.");
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextMaintenance || (maintenance != null && !maintenance.IsCompleted))
                return;

            nextMaintenance = now + MaintenanceInterval;
            maintenance = MaintainRoomAsync(room, IsRoomHost);
        }

        private void Begin(Func<CancellationToken, Task> command)
        {
            if (!isActiveAndEnabled || Busy || room != null)
                return;

            if (transport == null)
            {
                Status = "Online transport is unavailable.";
                return;
            }

            if (transport.IsSessionActive)
            {
                Status = "Leave the current session before going online.";
                return;
            }

            if (string.IsNullOrWhiteSpace(Application.cloudProjectId))
            {
                Status = "Online play needs a linked Unity Cloud project.";
                return;
            }

            operation = new CancellationTokenSource();
            _ = RunAsync(command, operation);
        }

        private async Task RunAsync(Func<CancellationToken, Task> command, CancellationTokenSource pending)
        {
            try
            {
                Status = "Connecting to Unity online services…";
                await EnsureSignedInAsync(pending.Token);
                await command(pending.Token);
            }
            catch (Exception) when (pending.IsCancellationRequested)
            {
                await CloseRoomAsync(IdleStatus);
            }
            catch (LobbyServiceException exception)
            {
                await CloseRoomAsync(LobbyFailure(exception.Reason));
            }
            catch (RelayServiceException exception)
            {
                await CloseRoomAsync(IsExpiredAllocation(exception.Reason)
                    ? "The host's relay connection has expired." : "Relay is unavailable. Try again.");
            }
            catch (RoomException exception)
            {
                await CloseRoomAsync(exception.Message);
            }
            catch (AuthenticationException)
            {
                await CloseRoomAsync("Online sign-in failed. Check Unity Authentication and try again.");
            }
            catch (RequestFailedException)
            {
                await CloseRoomAsync("Unity online services could not be reached. Try again.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Online session error " + exception.GetType().Name, this);
                await CloseRoomAsync("Could not open an online session.");
            }
            finally
            {
                FinishOperation(pending);
            }
        }

        private async Task EnsureSignedInAsync(CancellationToken token)
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();
            token.ThrowIfCancellationRequested();

            if (!AuthenticationService.Instance.IsAuthorized)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            token.ThrowIfCancellationRequested();
            localPlayerId = AuthenticationService.Instance.PlayerId;
        }

        private async Task QuickJoinAsync(CancellationToken token)
        {
            Status = "Looking for an online room…";
            try
            {
                room = await LobbyService.Instance.QuickJoinLobbyAsync(new QuickJoinLobbyOptions
                {
                    Filter = new List<QueryFilter>
                    {
                        new(QueryFilter.FieldOptions.S1, CompatibleBuild, QueryFilter.OpOptions.EQ)
                    }
                });
            }
            catch (LobbyServiceException exception) when (exception.Reason == LobbyExceptionReason.NoOpenLobbies)
            {
                token.ThrowIfCancellationRequested();
                await CreateAsync(false, token);
                return;
            }

            try
            {
                await OpenJoinedRoomAsync(token);
            }
            catch (RelayServiceException exception) when (IsExpiredAllocation(exception.Reason))
            {
                await CloseRoomAsync("Opening a new online room…");
                token.ThrowIfCancellationRequested();
                await CreateAsync(false, token);
            }
        }

        private async Task JoinByCodeAsync(string code, CancellationToken token)
        {
            Status = "Joining online room…";
            room = await LobbyService.Instance.JoinLobbyByCodeAsync(code);
            await OpenJoinedRoomAsync(token);
        }

        private async Task CreateAsync(bool privateRoom, CancellationToken token)
        {
            RequireIdleTransport(token);
            int capacity = transport.PlayerLimit;
            if (capacity < 2)
                throw new RoomException("Online rooms need room for at least two players.");

            Status = "Opening an online room…";
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(capacity - 1);
            token.ThrowIfCancellationRequested();
            string relayCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            RequireIdleTransport(token);
            RelayServerData relay = RelayData(allocation.ServerEndpoints, allocation.AllocationIdBytes,
                allocation.ConnectionData, allocation.ConnectionData, allocation.Key);

            if (!transport.StartRelayHost(relay))
                throw new RoomException("Could not start the online host.");

            ownsTransport = true;
            room = await LobbyService.Instance.CreateLobbyAsync("NUCLEUS VR", capacity, new CreateLobbyOptions
            {
                IsPrivate = privateRoom,
                Player = new Player(id: localPlayerId, allocationId: allocation.AllocationId.ToString()),
                Data = new Dictionary<string, DataObject>
                {
                    [ProtocolField] = new(DataObject.VisibilityOptions.Public, CompatibleBuild, DataObject.IndexOptions.S1),
                    [RelayField] = new(DataObject.VisibilityOptions.Member, relayCode)
                }
            });
            token.ThrowIfCancellationRequested();
            if (!HasRelayTransport)
                throw new RoomException("The online host connection closed before the room opened.");

            MarkConnected(privateRoom ? "Hosting private online room" : "Hosting public online room");
        }

        private async Task OpenJoinedRoomAsync(CancellationToken token)
        {
            RequireIdleTransport(token);
            if (room?.Data == null || !room.Data.TryGetValue(ProtocolField, out DataObject build) ||
                !string.Equals(build?.Value, CompatibleBuild, StringComparison.Ordinal))
                throw new RoomException("This room requires a different NUCLEUS VR build.");

            if (!room.Data.TryGetValue(RelayField, out DataObject relayCode) || string.IsNullOrWhiteSpace(relayCode?.Value))
                throw new RoomException("The host has not made a relay connection available.");

            Status = "Connecting to the host…";
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(relayCode.Value);
            RequireIdleTransport(token);
            RelayServerData relay = RelayData(allocation.ServerEndpoints, allocation.AllocationIdBytes,
                allocation.ConnectionData, allocation.HostConnectionData, allocation.Key);

            if (!transport.StartRelayClient(relay))
                throw new RoomException("Could not start the online connection.");

            ownsTransport = true;
            double deadline = Time.realtimeSinceStartupAsDouble + ConnectTimeout;
            while (transport.State != SessionState.Connected)
            {
                token.ThrowIfCancellationRequested();
                if (!HasRelayTransport || transport.State is SessionState.Failed or SessionState.Disconnecting)
                    throw new RoomException("The host closed the connection or the room is full.");

                if (Time.realtimeSinceStartupAsDouble >= deadline)
                    throw new RoomException("The host did not respond. Try joining again.");

                await Task.Delay(80, token);
            }

            token.ThrowIfCancellationRequested();
            await LobbyService.Instance.UpdatePlayerAsync(room.Id, localPlayerId, new UpdatePlayerOptions
            {
                AllocationId = allocation.AllocationId.ToString()
            });
            token.ThrowIfCancellationRequested();
            if (!HasRelayTransport || transport.State != SessionState.Connected)
                throw new RoomException("The host disconnected before the room was ready.");

            MarkConnected("Connected to online room");
        }

        private void MarkConnected(string status)
        {
            if (!OnlineRoomCode.TryNormalize(room.LobbyCode, out roomCode))
                throw new RoomException("The service did not return a usable room code.");

            hasConnected = true;
            maintenanceFailures = 0;
            nextMaintenance = Time.realtimeSinceStartupAsDouble + MaintenanceInterval;
            Status = status;
        }

        private void RequireIdleTransport(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (transport == null || transport.IsSessionActive)
                throw new RoomException("The current transport is occupied by another session.");
        }

        private async Task MaintainRoomAsync(Lobby active, bool host)
        {
            try
            {
                if (host)
                {
                    await LobbyService.Instance.SendHeartbeatPingAsync(active.Id);
                }
                else
                {
                    Lobby current = await LobbyService.Instance.GetLobbyAsync(active.Id);
                    bool sameHost = current?.HostId == active.HostId;
                    bool isMember = current?.Players?.Exists(player => player?.Id == localPlayerId) == true;
                    if (ReferenceEquals(room, active) && (!sameHost || !isMember))
                    {
                        StartClose("The room host left or your membership ended.");
                        return;
                    }
                }

                if (ReferenceEquals(room, active))
                    maintenanceFailures = 0;
            }
            catch (LobbyServiceException exception) when (IsRoomGone(exception.Reason))
            {
                if (ReferenceEquals(room, active))
                    StartClose("The host closed the online room.");
            }
            catch (Exception)
            {
                if (!ReferenceEquals(room, active))
                    return;

                maintenanceFailures++;
                if (maintenanceFailures >= 3)
                    StartClose("The online room connection was lost.");
                else
                    nextMaintenance = Time.realtimeSinceStartupAsDouble + MaintenanceRetry;
            }
        }

        private void StartClose(string finalStatus)
        {
            if (operation != null)
                return;

            operation = new CancellationTokenSource();
            _ = RunCloseAsync(finalStatus, operation);
        }

        private async Task RunCloseAsync(string finalStatus, CancellationTokenSource pending)
        {
            try
            {
                await CloseRoomAsync(finalStatus);
            }
            finally
            {
                FinishOperation(pending);
            }
        }

        private async Task CloseRoomAsync(string finalStatus)
        {
            Lobby closing = room;
            bool host = IsRoomHost;
            string playerId = localPlayerId;
            hasConnected = false;
            room = null;
            roomCode = "";
            StopTransport();
            Status = "Leaving online room…";
            if (closing != null)
            {
                try
                {
                    if (host)
                        await LobbyService.Instance.DeleteLobbyAsync(closing.Id);
                    else
                        await LobbyService.Instance.RemovePlayerAsync(closing.Id, playerId);
                }
                catch (LobbyServiceException exception) when (IsRoomGone(exception.Reason))
                {
                    // A room can disappear as the local transport closes.
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Lobby cleanup error " + exception.GetType().Name, this);
                    if (finalStatus == IdleStatus)
                        finalStatus = "Disconnected. Online room cleanup could not be confirmed.";
                }
            }

            Status = finalStatus;
        }

        private void StopTransport()
        {
            bool shouldDisconnect = HasRelayTransport;
            ownsTransport = false;
            if (!shouldDisconnect)
                return;

            try
            {
                transport.Disconnect();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Relay cleanup error " + exception.GetType().Name, this);
            }
        }

        private void FinishOperation(CancellationTokenSource pending)
        {
            if (ReferenceEquals(operation, pending))
                operation = null;
            pending.Dispose();
        }

        private static RelayServerData RelayData(List<RelayServerEndpoint> endpoints, byte[] allocationId,
            byte[] connectionData, byte[] hostConnectionData, byte[] key)
        {
            RelayServerEndpoint endpoint = endpoints?.Find(candidate =>
                candidate != null && candidate.ConnectionType == RelayProtocol && candidate.Secure &&
                candidate.Port > 0 && candidate.Port <= ushort.MaxValue && !string.IsNullOrWhiteSpace(candidate.Host));
            if (endpoint == null)
                throw new RoomException("Relay did not provide a secure DTLS endpoint.");

            // This public Transport overload works with the unified Lobby and Relay SDK.
            return new RelayServerData(endpoint.Host, (ushort)endpoint.Port, allocationId,
                connectionData, hostConnectionData, key, true);
        }

        private static bool IsRoomGone(LobbyExceptionReason reason) =>
            reason is LobbyExceptionReason.LobbyNotFound or LobbyExceptionReason.PlayerNotFound;

        private static bool IsExpiredAllocation(RelayExceptionReason reason) =>
            reason is RelayExceptionReason.AllocationNotFound or RelayExceptionReason.JoinCodeNotFound;

        private static string LobbyFailure(LobbyExceptionReason reason)
        {
            return reason switch
            {
                LobbyExceptionReason.LobbyFull => "This online room is full.",
                LobbyExceptionReason.LobbyLocked => "The host has locked this room.",
                LobbyExceptionReason.LobbyNotFound or LobbyExceptionReason.InvalidJoinCode => "No online room matches that code.",
                LobbyExceptionReason.RateLimited => "Too many online requests. Wait a moment and try again.",
                LobbyExceptionReason.Forbidden or LobbyExceptionReason.Unauthorized => "Check that Lobby and Relay are enabled for this Unity Cloud project.",
                _ => "Lobby is unavailable. Try again."
            };
        }

        private sealed class RoomException : Exception
        {
            public RoomException(string message) : base(message) { }
        }
    }
}
