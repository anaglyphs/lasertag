/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Fusion;
using Fusion.Sockets;
using System;
using System.Buffers;
using System.Collections.Generic;
using UnityEngine;

namespace Meta.XR.MultiplayerBlocks.Fusion
{
    /// <summary>
    /// A helper class to easily expose the Photon Fusion callbacks to other Building Blocks. For a full documentation
    /// of all the exposed callbacks please see the <see cref="INetworkRunnerCallbacks"/> documentation page
    /// https://doc-api.photonengine.com/en/fusion/v2/interface_fusion_1_1_i_network_runner_callbacks.html.
    /// </summary>
    public class FusionBBEvents : MonoBehaviour, INetworkRunnerCallbacks, IDisposable
    {
        // The INetworkRunnerCallbacks interface has to be placed in the root of the
        // network runner. This helper allows us to share these events
        public static event Action<NetworkRunner> OnConnectedToServer;
        public static event Action<NetworkRunner, PlayerRef> OnPlayerJoined;
        public static event Action<NetworkRunner, NetworkInput> OnInput;
        public static event Action<NetworkRunner, NetAddress, NetConnectFailedReason> OnConnectFailed;
        public static event Action<NetworkRunner, NetworkRunnerCallbackArgs.ConnectRequest, byte[]> OnConnectRequest;
        public static event Action<NetworkRunner, Dictionary<string, object>> OnCustomAuthenticationResponse;
        public static event Action<NetworkRunner, HostMigrationToken> OnHostMigration;
        public static event Action<NetworkRunner, PlayerRef, NetworkInput> OnInputMissing;
        public static event Action<NetworkRunner, PlayerRef> OnPlayerLeft;
        public static event Action<NetworkRunner> OnSceneLoadDone;
        public static event Action<NetworkRunner> OnSceneLoadStart;
        public static event Action<NetworkRunner, List<SessionInfo>> OnSessionListUpdated;
        public static event Action<NetworkRunner, ShutdownReason> OnShutdown;
        public static event Action<NetworkRunner, SimulationMessagePtr> OnUserSimulationMessage;
        public static event Action<NetworkRunner, NetworkObject, PlayerRef> OnObjectExitAOI;
        public static event Action<NetworkRunner, NetworkObject, PlayerRef> OnObjectEnterAOI;
        public static event Action<NetworkRunner, NetDisconnectReason> OnDisconnectedFromServer;


#if FUSION_2_1_OR_NEWER
        private byte[] _sharedDataReceivedArray;
        public static event Action<NetworkRunner, PlayerRef, ReliableKey, ReadOnlyMemory<byte>> OnReliableDataReceived;
#else // FUSION_2_1_OR_NEWER
        public static event Action<NetworkRunner, PlayerRef, ReliableKey, ArraySegment<byte>> OnReliableDataReceived;
#endif // FUSION_2_1_OR_NEWER
        public static event Action<NetworkRunner, PlayerRef, ReliableKey, float> OnReliableDataProgress;

        void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner)
        {
            OnConnectedToServer?.Invoke(runner);
        }

        void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            OnPlayerJoined?.Invoke(runner, player);
        }

        void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input)
        {
            OnInput?.Invoke(runner, input);
        }

        void INetworkRunnerCallbacks.OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress,
            NetConnectFailedReason reason)
        {
            Debug.LogWarning(nameof(INetworkRunnerCallbacks.OnConnectFailed));
            OnConnectFailed?.Invoke(runner, remoteAddress, reason);
        }

        void INetworkRunnerCallbacks.OnConnectRequest(NetworkRunner runner,
            NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
        {
            Debug.LogWarning(nameof(INetworkRunnerCallbacks.OnConnectRequest));
            OnConnectRequest?.Invoke(runner, request, token);
        }

        void INetworkRunnerCallbacks.OnCustomAuthenticationResponse(NetworkRunner runner,
            Dictionary<string, object> data)
        {
            OnCustomAuthenticationResponse?.Invoke(runner, data);
        }

        void INetworkRunnerCallbacks.OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
        {
            OnHostMigration?.Invoke(runner, hostMigrationToken);
        }

        void INetworkRunnerCallbacks.OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
        {
            OnInputMissing?.Invoke(runner, player, input);
        }

        void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            OnPlayerLeft?.Invoke(runner, player);
        }

        void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner runner)
        {
            OnSceneLoadDone?.Invoke(runner);
        }

        void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner runner)
        {
            OnSceneLoadStart?.Invoke(runner);
        }

        void INetworkRunnerCallbacks.OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
        {
            OnSessionListUpdated?.Invoke(runner, sessionList);
        }

        void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            OnShutdown?.Invoke(runner, shutdownReason);
        }

        void INetworkRunnerCallbacks.OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)
        {
            OnUserSimulationMessage?.Invoke(runner, message);
        }

        void INetworkRunnerCallbacks.OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
        {
            OnObjectExitAOI?.Invoke(runner, obj, player);
        }

        void INetworkRunnerCallbacks.OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
        {
            OnObjectEnterAOI?.Invoke(runner, obj, player);
        }

        void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            OnDisconnectedFromServer?.Invoke(runner, reason);
        }

#if FUSION_2_1_OR_NEWER
        void INetworkRunnerCallbacks.OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key,
            ReadOnlySpan<byte> data)
        {
            if (_sharedDataReceivedArray != null)
            {
                ArrayPool<byte>.Shared.Return(_sharedDataReceivedArray, clearArray: true);
                _sharedDataReceivedArray = null;
            }
            _sharedDataReceivedArray = ArrayPool<byte>.Shared.Rent(data.Length);
            data.CopyTo(_sharedDataReceivedArray);
            OnReliableDataReceived?.Invoke(runner, player, key, _sharedDataReceivedArray.AsMemory(0, data.Length));
        }
#else // FUSION_2_1_OR_NEWER
        void INetworkRunnerCallbacks.OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key,
            ArraySegment<byte> data)
        {
            OnReliableDataReceived?.Invoke(runner, player, key, data);
        }
#endif // FUSION_2_1_OR_NEWER

        void INetworkRunnerCallbacks.OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key,
            float progress)
        {
            OnReliableDataProgress?.Invoke(runner, player, key, progress);
        }

        public void Dispose()
        {
#if FUSION_2_1_OR_NEWER
            if (_sharedDataReceivedArray == null)
            {
                return;
            }
            ArrayPool<byte>.Shared.Return(_sharedDataReceivedArray, clearArray: true);
#endif // FUSION_2_1_OR_NEWER
        }
    }
}
