using Mirror;
using TOP.Player;  // ✅ CORREÇÃO: era TOP.Gameplay

namespace TOP.Network
{
    public enum ConnectionState
    {
        Login,
        CharacterSelect,
        InGame
    }

    public class PlayerConnection
    {
        public int ConnectionId;
        public ConnectionState State;
        public long AccountId;
        public long CharacterId;
        public string Username;
        public string SessionToken;
        public NetworkConnectionToClient Connection;
        public PlayerController PlayerController;  // ✅ TOP.Player.PlayerController
        public RateLimiter RateLimiter;
        public float LastPingTime;
        public float ConnectTime;
    }

    public class RateLimiter
    {
        private readonly System.Collections.Generic.Queue<float> _timestamps = new System.Collections.Generic.Queue<float>();
        private readonly int _maxRequests;
        private readonly float _timeWindow;

        public RateLimiter(int maxRequests, float timeWindow)
        {
            _maxRequests = maxRequests;
            _timeWindow = timeWindow;
        }

        public bool CanProcess()
        {
            float now = UnityEngine.Time.time;
            while (_timestamps.Count > 0 && _timestamps.Peek() < now - _timeWindow)
                _timestamps.Dequeue();

            if (_timestamps.Count >= _maxRequests)
                return false;

            _timestamps.Enqueue(now);
            return true;
        }
    }
}