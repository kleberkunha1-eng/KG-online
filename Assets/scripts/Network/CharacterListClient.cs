using System;
using Mirror;
using UnityEngine;

namespace TOP.Network
{
    public static class CharacterListClient
    {
        public static event Action<CharacterListResponse> Received;
        public static bool HasResponse { get; private set; }
        public static CharacterListResponse Response { get; private set; }
        static bool pending;
        static float started;

        public static void Reset()
        {
            pending = false;
            HasResponse = false;
            Response = default;
        }

        public static void Request()
        {
            if (pending) return;
            if (!NetworkClient.isConnected || NetworkClient.connection == null)
                throw new InvalidOperationException("Character list requires a connected authenticated session.");
            HasResponse = false;
            pending = true;
            started = Time.realtimeSinceStartup;
            NetworkClient.RegisterHandler<CharacterListResponse>(OnResponse);
            NetworkClient.Send(new CharacterListRequest());
        }

        static void OnResponse(CharacterListResponse response)
        {
            pending = false;
            Response = response;
            HasResponse = true;
            Debug.Log($"[Loading] Character list network/API response: {Time.realtimeSinceStartup - started:F2}s.");
            Received?.Invoke(response);
        }
    }
}
