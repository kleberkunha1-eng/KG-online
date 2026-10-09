using System;
using System.Collections;
using Mirror;
using TOP.World;
using TOP.Services;
using UnityEngine;

namespace TOP.Network
{
    public partial class TOPNetworkManager
    {
        EnvironmentControls environmentControls;
        double environmentOffset;
        readonly System.Collections.Generic.HashSet<int> environmentRequests =
            new System.Collections.Generic.HashSet<int>();

        void StartEnvironmentServer()
        {
            var settings = Resources.Load<WorldEnvironmentSettings>("WorldEnvironment");
            if (settings == null || !settings.defaults.IsValid)
            {
                Debug.LogError("[Environment] Configuracao WorldEnvironment ausente ou invalida.");
                return;
            }
            environmentControls = settings.defaults;
            environmentOffset = 0;
            NetworkServer.RegisterHandler<EnvironmentChange>(OnEnvironmentChange);
            InvokeRepeating(nameof(BroadcastEnvironment), 0, 2);
        }

        EnvironmentSnapshot EnvironmentState()
        {
            var now = DateTimeOffset.Now;
            return new EnvironmentSnapshot
            {
                localSeconds = now.ToUnixTimeMilliseconds() / 1000d + now.Offset.TotalSeconds + environmentOffset,
                networkTime = NetworkTime.time,
                utcOffsetMinutes = (int)now.Offset.TotalMinutes,
                controls = environmentControls
            };
        }

        void BroadcastEnvironment()
        {
            var state = EnvironmentState();
            foreach (var player in _connections.Values)
                if (IsAuthenticated(player.ConnectionId, out _, out _))
                    player.Connection.Send(state);
        }

        void OnEnvironmentChange(NetworkConnectionToClient conn, EnvironmentChange request)
        {
            if (!IsAuthenticated(conn.connectionId, out var player, out var error))
            {
                EnvironmentResult(conn, false, error);
                return;
            }
            if (!CheckRateLimit(conn.connectionId) || environmentRequests.Contains(conn.connectionId))
            {
                EnvironmentResult(conn, false, "Aguarde o pedido anterior ou reduza a frequencia.");
                return;
            }
            if (!request.controls.IsValid || float.IsNaN(request.hour) || float.IsInfinity(request.hour)
                || request.hour < 0 || request.hour >= 24)
            {
                EnvironmentResult(conn, false, "Parametros de ambiente invalidos.");
                return;
            }
            environmentRequests.Add(conn.connectionId);
            StartCoroutine(AuthorizeEnvironment(conn, player.AccountId, player.SessionToken, request));
        }

        IEnumerator AuthorizeEnvironment(NetworkConnectionToClient conn, long accountId, string token,
            EnvironmentChange request)
        {
            var database = DatabaseService.Instance;
            if (database == null)
            {
                environmentRequests.Remove(conn.connectionId);
                EnvironmentResult(conn, false, "Servico de dados indisponivel.");
                yield break;
            }
            var authorization = database.IsAdminAsync(accountId, token);
            while (!authorization.IsCompleted) yield return null;
            environmentRequests.Remove(conn.connectionId);
            if (!NetworkServer.active || !IsAuthenticated(conn.connectionId, out var player, out _)
                || player.AccountId != accountId || player.SessionToken != token) yield break;
            if (authorization.IsFaulted || authorization.IsCanceled)
            {
                Debug.LogError("[Environment] Falha ao validar admin: " + authorization.Exception);
                EnvironmentResult(conn, false, "A API nao confirmou a permissao.");
                yield break;
            }
            if (!authorization.Result)
            {
                EnvironmentResult(conn, false, "Somente administradores podem alterar o ambiente.");
                yield break;
            }
            var current = EnvironmentState();
            if (request.restoreClock) environmentOffset = 0;
            else
            {
                double delta = request.hour * 3600 - EnvironmentCycle.Repeat(current.localSeconds, 86400);
                if (delta > 43200) delta -= 86400;
                if (delta < -43200) delta += 86400;
                environmentOffset += delta;
            }
            environmentControls = request.controls;
            BroadcastEnvironment();
            EnvironmentResult(conn, true, "Ambiente atualizado para todos; ciclo continua em tempo real.");
        }

        static void EnvironmentResult(NetworkConnectionToClient conn, bool success, string message)
        {
            if (!success) Debug.LogWarning("[Environment] " + message);
            conn.Send(new EnvironmentReply { success = success, message = message });
        }
    }
}
