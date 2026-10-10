using System.Collections.Generic;
using Mirror;
using TOP.Player;

namespace TOP.Network
{
    public sealed class ArenaInterestManagement : InterestManagement
    {
        public static int Room(NetworkIdentity identity)
        {
            var controller = identity != null ? identity.GetComponent<PlayerController>() : null;
            return controller != null ? controller.ArenaInstanceId : 0;
        }
        public override bool OnCheckObserver(NetworkIdentity identity, NetworkConnectionToClient observer)
            => observer != null && observer.identity != null && Room(identity) == Room(observer.identity);
        public override void OnRebuildObservers(NetworkIdentity identity, HashSet<NetworkConnectionToClient> observers)
        {
            foreach (var connection in NetworkServer.connections.Values)
                if (connection.isReady && OnCheckObserver(identity, connection)) observers.Add(connection);
        }
        public void Refresh() { if (NetworkServer.active) RebuildAll(); }
    }
}
