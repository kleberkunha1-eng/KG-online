using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using TOP.Network;

namespace TOP.Admin
{
    // Cliente HTTP da API (JWT do login). Todo acesso a dados do jogo passa por aqui.
    public class ApiClient : MonoBehaviour
    {
        public static string BaseUrl => TOP.Services.ApiConfig.AdminUrl;
        static ApiClient _inst;

        public static ApiClient Instance
        {
            get
            {
                if (_inst == null) { var go = new GameObject("ApiClient"); DontDestroyOnLoad(go); _inst = go.AddComponent<ApiClient>(); }
                return _inst;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { var _ = Instance; }

        public void Request(string method, string path, string json, Action<bool, string> done)
        {
            StartCoroutine(Run(method, path, json, done));
        }

        IEnumerator Run(string method, string path, string json, Action<bool, string> done)
        {
            using var req = new UnityWebRequest(BaseUrl + path, method);
            if (json != null) { req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)); req.SetRequestHeader("Content-Type", "application/json"); }
            req.downloadHandler = new DownloadHandlerBuffer();
            if (LoginNetworkClient.IsLoggedIn) req.SetRequestHeader("Authorization", "Bearer " + LoginNetworkClient.AuthToken);
            req.timeout = 10;
            double traceStarted = TOP.Diagnostics.GameTrace.Now;
            yield return req.SendWebRequest();
            TOP.Diagnostics.GameTrace.Http(req, traceStarted);
            bool ok = req.result == UnityWebRequest.Result.Success;
            done?.Invoke(ok, ok ? req.downloadHandler.text : (req.downloadHandler.text is { Length: > 0 } t ? t : req.error));
        }

    }
}