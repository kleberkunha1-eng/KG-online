using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using TOP.Core;
using TOP.UI.Pko;
using TOP.Network;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[InitializeOnLoad]
public static class LoginInteractionValidation
{
    const string Request = "Tools/validate-login-interaction.request";
    static LoginInteractionValidation() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        Run();
    }

    static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    static MethodInfo Method(Type type, string name) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);

    [MenuItem("Tools/PKO/Validate Login Interaction")]
    public static void Run()
    {
        var report = new StringBuilder();
        int passed = 0, failed = 0;
        Action<bool, string> check = (ok, text) =>
        {
            report.AppendLine((ok ? "PASS " : "FAIL ") + text);
            if (ok) passed++; else failed++;
        };
        var test = new GameObject("LoginInteractionTest");
        GameObject eventObject = null;
        PkoUi newUi = null;
        var events = EventSystem.current;
        var selected = events != null ? events.currentSelectedGameObject : null;
        var priority = Application.backgroundLoadingPriority;
        try
        {
            if (events == null)
            {
                eventObject = new GameObject("TestEvents", typeof(EventSystem));
                events = eventObject.GetComponent<EventSystem>();
                Method(typeof(EventSystem), "OnEnable").Invoke(events, null);
            }
            var flow = test.AddComponent<PkoFlow>();
            var canvasObject = new GameObject("LoginTestCanvas", typeof(Canvas));
            canvasObject.transform.SetParent(test.transform, false);
            Field(typeof(PkoFlow), "loginCanvas").SetValue(flow, canvasObject.GetComponent<Canvas>());
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Method(typeof(PkoFlow), "BuildLoginCard").Invoke(flow, null);
            watch.Stop();
            report.AppendLine($"Login card construction={watch.Elapsed.TotalMilliseconds:F1}ms");
            var user = (InputField)Field(typeof(PkoFlow), "idField").GetValue(flow);
            var password = (InputField)Field(typeof(PkoFlow), "pwField").GetValue(flow);
            check(user.IsInteractable() && password.IsInteractable(), "Username and password accept input immediately");
            check(password.contentType == InputField.ContentType.Password, "Password remains concealed");
            var connectionStatus = new GameObject("ConnectionStatus", typeof(Text));
            connectionStatus.transform.SetParent(test.transform, false);
            Field(typeof(PkoFlow), "status").SetValue(flow, connectionStatus.GetComponent<Text>());
            Field(typeof(PkoFlow), "busy").SetValue(flow, true);
            Method(typeof(PkoFlow), "OnGameConnectionFailed").Invoke(flow, new object[] { "Servidor do jogo indisponivel." });
            check(!(bool)Field(typeof(PkoFlow), "busy").GetValue(flow),
                "Transport failure releases login for a new attempt.");
            check(connectionStatus.GetComponent<Text>().text == "Servidor do jogo indisponivel.",
                "Transport failure is visible in login status, not only the Console.");
            Method(typeof(PkoFlow), "OnGameConnectionFailed").Invoke(flow, new object[] { "Duplicate disconnect." });
            check(connectionStatus.GetComponent<Text>().text == "Servidor do jogo indisponivel.",
                "Duplicate disconnect does not replace the original connection error.");
            check(Field(typeof(PkoFlow), "registerCard").GetValue(flow) == null, "Registration UI is not constructed before requested");
            events.SetSelectedGameObject(user.gameObject);
            var tab = Method(typeof(PkoFlow), "FocusNextField");
            tab.Invoke(flow, new object[] { false });
            check(events.currentSelectedGameObject == password.gameObject, "Tab moves username to password");
            tab.Invoke(flow, new object[] { false });
            check(events.currentSelectedGameObject == user.gameObject, "Tab wraps password to username");
            tab.Invoke(flow, new object[] { true });
            check(events.currentSelectedGameObject == password.gameObject, "Shift+Tab navigates backwards");
            Method(typeof(PkoFlow), "OpenRegister").Invoke(flow, null);
            var account = (InputField)Field(typeof(PkoFlow), "regIdField").GetValue(flow);
            var email = (InputField)Field(typeof(PkoFlow), "regEmailField").GetValue(flow);
            check(events.currentSelectedGameObject == account.gameObject, "Opening registration focuses account");
            tab.Invoke(flow, new object[] { true });
            check(events.currentSelectedGameObject == email.gameObject, "Registration reverse Tab wraps to email");
            CharacterListClient.Reset();
            var list = new CharacterListResponse
            {
                Success = true,
                Characters = new[] { new NetworkCharacterPreview { Id = 777, Name = "PreviewTest" } }
            };
            typeof(CharacterListClient).GetMethod("OnResponse", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { list });
            check(CharacterListClient.HasResponse && CharacterListClient.Response.Characters[0].Id == 777,
                "Character list arriving before scene is cached for selection");
            typeof(CharacterListClient).GetField("pending", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, true);
            CharacterListClient.Request();
            check((bool)typeof(CharacterListClient).GetField("pending", BindingFlags.Static | BindingFlags.NonPublic)
                .GetValue(null), "Pending character request is not sent twice");
            CharacterListClient.Reset();
            check(!CharacterListClient.HasResponse && CharacterListClient.Response.Characters == null,
                "Reconnection clears previous account character cache");

            if (PkoUi.Instance == null)
            {
                var uiObject = new GameObject("LoginValidationUi");
                newUi = uiObject.AddComponent<PkoUi>();
                Method(typeof(PkoUi), "Awake").Invoke(newUi, null);
            }
            var ui = PkoUi.Instance;
            var gameUi = test.AddComponent<PkoUiGame>();
            Field(typeof(PkoUiGame), "ui").SetValue(gameUi, ui);
            int initial = ui.Windows.Count;
            var window = ui.Get("frmInv");
            check(ui.Windows.Count <= initial + 1, "On-demand access builds only the requested window");
            check(ui.Get("frmInv") == window && ui.Windows.Count <= initial + 1,
                "Repeated access reuses the existing window without rebuilding");
            if (GameFlowManager.Instance == null)
            {
                var manager = test.AddComponent<GameFlowManager>();
                Field(typeof(GameFlowManager), "previousLoadingPriority").SetValue(manager, priority);
                Field(typeof(GameFlowManager), "loadingPriorityChanged").SetValue(manager, true);
                Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.High;
                Method(typeof(GameFlowManager), "OnWorldLoaded").Invoke(manager, new object[] { null });
                check(Application.backgroundLoadingPriority == priority, "Loading priority is restored after world transition");
            }
        }
        catch (Exception e) { Debug.LogException(e); check(false, e.ToString()); }
        finally
        {
            Application.backgroundLoadingPriority = priority;
            if (events != null) events.SetSelectedGameObject(selected);
            UnityEngine.Object.DestroyImmediate(test);
            if (newUi != null) UnityEngine.Object.DestroyImmediate(newUi.gameObject);
            if (eventObject != null)
            {
                Method(typeof(EventSystem), "OnDisable").Invoke(events, null);
                UnityEngine.Object.DestroyImmediate(eventObject);
            }
        }
        report.Insert(0, $"Passed={passed} Failed={failed}\n");
        File.WriteAllText("Tools/login-interaction-validation-results.txt", report.ToString());
        if (failed > 0) Debug.LogError(report.ToString()); else Debug.Log(report.ToString());
    }
}
