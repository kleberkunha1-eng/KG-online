using System;
using System.IO;
using System.Reflection;
using System.Text;
using TOP.UI;
using TOP.UI.Pko;
using TOP.Player;
using TOP.Data;
using Mirror;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[InitializeOnLoad]
public static class WindowControlsValidation
{
    static WindowControlsValidation() { EditorApplication.update += Poll; }
    static void Poll()
    {
        const string request = "Tools/validate-window-controls.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(request);
        Run();
    }

    static FieldInfo Field(Type type, string name, bool isStatic = false) =>
        type.GetField(name, BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance));

    [MenuItem("Tools/PKO/Validate Window Controls")]
    public static void Run()
    {
        int passed = 0, failed = 0;
        var report = new StringBuilder();
        Action<bool, string> check = (ok, text) =>
        {
            report.AppendLine((ok ? "PASS " : "FAIL ") + text);
            if (ok) passed++; else failed++;
        };
        var root = new GameObject("WindowControlsTest");
        var existingEvents = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
        EventSystem createdEvents = null;
        PkoUi ui = null;
        object oldGuild = Field(typeof(GuildUI), "instance", true).GetValue(null);
        object oldFriends = Field(typeof(FriendsUI), "instance", true).GetValue(null);
        object oldQuests = Field(typeof(QuestLogUI), "instance", true).GetValue(null);
        object oldParty = Field(typeof(PartyUI), "instance", true).GetValue(null);
        object oldMail = Field(typeof(MailUI), "instance", true).GetValue(null);
        object oldTrade = Field(typeof(TradeUI), "instance", true).GetValue(null);
        try
        {
            ui = root.AddComponent<PkoUi>();
            typeof(PkoUi).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui, null);
            if (existingEvents == null) createdEvents = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            foreach (var entry in ui.Defs)
            {
                if (entry.Key != entry.Value.name) continue;
                var window = ui.Get(entry.Key);
                var close = window.transform.Find("WindowClose")?.GetComponent<Button>();
                check(close != null && close.interactable, entry.Key + " has a usable X");
                window.Open();
                close?.onClick.Invoke();
                check(!window.IsOpen, entry.Key + " closes through X");
            }
            check(ui.ActivateShortcut(KeyCode.E, true, false, false)
                && ui.Get("frmInv").IsOpen, "Alt+E opens inventory");
            ui.ActivateShortcut(KeyCode.E, true, false, false);
            check(!ui.Get("frmInv").IsOpen, "Alt+E toggles inventory closed");
            check(!ui.ActivateShortcut(KeyCode.E, true, true, false), "Conflicting modifiers do not activate inventory");
            var gameObject = new GameObject("InventorySessionTest");
            var playerA = new GameObject("InventoryCharacterA", typeof(NetworkIdentity), typeof(PlayerInventory), typeof(PlayerController));
            var playerB = new GameObject("InventoryCharacterB", typeof(NetworkIdentity), typeof(PlayerInventory), typeof(PlayerController));
            try
            {
                var game = gameObject.AddComponent<PkoUiGame>();
                Field(typeof(PkoUiGame), "ui").SetValue(game, ui);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var bar = typeof(PkoUiGame).GetMethod("Bar", BindingFlags.Static | BindingFlags.NonPublic);
                var audio = ui.Get("frmAudio");
                var progress = audio.Get<PkoProgress>("proAudioMusic");
                bar.Invoke(null, new object[] { audio, "proAudioMusic", .25f, new Action<float>(_ => { }) });
                bar.Invoke(null, new object[] { audio, "proAudioMusic", .75f, new Action<float>(_ => { }) });
                check(progress.GetComponents<PkoClickBar>().Length == 1
                    && progress.GetComponents<Graphic>().Length == 1,
                    "Repeated audio setup reuses its click handler and Graphic without aborting HUD initialization");
                typeof(PkoUiGame).GetMethod("SetupInventory", flags).Invoke(game, null);
                var inventoryWindow = ui.Get("frmInv");
                inventoryWindow.Open();
                var deserialize = typeof(PlayerInventory).GetMethod("DeserializeInventory", flags);
                deserialize.Invoke(playerA.GetComponent<PlayerInventory>(), new object[] { "0:990010:1:0:0:-1:-1:-1:100" });
                deserialize.Invoke(playerB.GetComponent<PlayerInventory>(), new object[] { "1:990014:2:0:0:-1:-1:-1:100" });
                var bind = typeof(PkoUiGame).GetMethod("BindPlayer", flags);
                bind.Invoke(game, new object[] { playerA.GetComponent<NetworkIdentity>() });
                var grid = inventoryWindow.Grids["grdItem"];
                check(grid[0].Filled && !grid[1].Filled, "First character shows only its own inventory slots");
                bind.Invoke(game, new object[] { playerB.GetComponent<NetworkIdentity>() });
                check(!grid[0].Filled && grid[1].Filled && grid[1].Count.text == "2",
                    "Switching character clears old icons and shows only the second character's items");
                bind.Invoke(game, new object[] { null });
                check(!grid[0].Filled && !grid[1].Filled, "Disconnecting clears character inventory from the persistent window");
                Field(typeof(PkoUiGame), "ui").SetValue(game, null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                UnityEngine.Object.DestroyImmediate(playerA);
                UnityEngine.Object.DestroyImmediate(playerB);
            }
            var oldInventory = ui.Get("frmInv");
            var oldAudio = ui.Get("frmAudio");
            ui.ReleaseGameWindows();
            check(!ui.Windows.ContainsKey("frmInv") && !oldInventory.IsOpen,
                "Leaving the world releases cached gameplay windows and their previous session callbacks");
            check(ui.Get("frmInv") != oldInventory && ui.Get("frmAudio") != oldAudio,
                "Next character gets fresh inventory and audio windows");
            check(ui.ActivateShortcut(KeyCode.E, true, false, false) && ui.Get("frmInv").IsOpen,
                "Alt+E still opens inventory after character-session teardown");
            ui.Get("frmInv").Close();
            var guild = root.AddComponent<GuildUI>();
            Field(typeof(GuildUI), "instance", true).SetValue(null, guild);
            Field(typeof(GuildUI), "local").SetValue(guild, root.AddComponent<PlayerGuild>());
            Field(typeof(GuildUI), "open").SetValue(guild, true);
            check(ui.ActivateShortcut(KeyCode.G, true, false, false)
                && !(bool)Field(typeof(GuildUI), "open").GetValue(guild), "Alt+G reaches real guild window");
            Field(typeof(GuildUI), "open").SetValue(guild, true);
            ui.ActivateShortcut(KeyCode.C, true, false, false);
            check(!(bool)Field(typeof(GuildUI), "open").GetValue(guild), "Original Alt+C reaches real guild window");
            var friends = root.AddComponent<FriendsUI>();
            Field(typeof(FriendsUI), "instance", true).SetValue(null, friends);
            Field(typeof(FriendsUI), "local").SetValue(friends, root.AddComponent<PlayerFriends>());
            Field(typeof(FriendsUI), "open").SetValue(friends, true);
            ui.ActivateShortcut(KeyCode.F, true, false, false);
            check(!(bool)Field(typeof(FriendsUI), "open").GetValue(friends), "Alt+F reaches real friends window");
            Field(typeof(FriendsUI), "open").SetValue(friends, true);
            check(ui.ActivateShortcut(KeyCode.N, true, false, false)
                && !(bool)Field(typeof(FriendsUI), "open").GetValue(friends), "Alt+N uses the same friends action");
            var quests = root.AddComponent<QuestLogUI>();
            Field(typeof(QuestLogUI), "instance", true).SetValue(null, quests);
            Field(typeof(QuestLogUI), "local").SetValue(quests, root.AddComponent<PlayerQuests>());
            ui.ActivateShortcut(KeyCode.Q, true, false, false);
            check((bool)Field(typeof(QuestLogUI), "open").GetValue(quests), "Alt+Q reaches real quest window");
            check(ui.ActivateShortcut(KeyCode.J, true, false, false)
                && !(bool)Field(typeof(QuestLogUI), "open").GetValue(quests), "Alt+J uses the same quests action");
            var party = root.AddComponent<PartyUI>();
            Field(typeof(PartyUI), "instance", true).SetValue(null, party);
            Field(typeof(PartyUI), "localParty").SetValue(party, root.AddComponent<PlayerParty>());
            ui.ActivateShortcut(KeyCode.P, true, false, false);
            check((bool)Field(typeof(PartyUI), "panelOpen").GetValue(party), "Alt+P reaches real party window");
            var mail = root.AddComponent<MailUI>();
            Field(typeof(MailUI), "instance", true).SetValue(null, mail);
            Field(typeof(MailUI), "local").SetValue(mail, root.AddComponent<PlayerMail>());
            Field(typeof(MailUI), "open").SetValue(mail, true);
            check(ui.ActivateShortcut(KeyCode.M, true, false, false)
                && !(bool)Field(typeof(MailUI), "open").GetValue(mail), "Alt+M reaches real mail window");
            var trade = root.AddComponent<TradeUI>();
            Field(typeof(TradeUI), "instance", true).SetValue(null, trade);
            Field(typeof(TradeUI), "local").SetValue(trade, root.AddComponent<PlayerTrade>());
            Field(typeof(TradeUI), "open").SetValue(trade, true);
            check(ui.ActivateShortcut(KeyCode.Y, true, false, false)
                && !(bool)Field(typeof(TradeUI), "open").GetValue(trade), "Alt+Y toggles trade without sending a new invitation");
            Field(typeof(GuildUI), "open").SetValue(guild, true);
            check(ui.ActivateShortcut(KeyCode.G, false, false, false)
                && !(bool)Field(typeof(GuildUI), "open").GetValue(guild), "Legacy G remains supported without Alt");
            Field(typeof(GameWindowControls), "blockedFrame", true).SetValue(null, Time.frameCount + 100);
            check(!GameWindowControls.BlocksMouse, "A previous play session cannot permanently block movement");
            Field(typeof(GameWindowControls), "blockedFrame", true).SetValue(null, -2);
            var admin = root.AddComponent<TOP.Admin.AdminPanel>();
            Field(typeof(TOP.Admin.AdminPanel), "pendingRequestId").SetValue(admin, 42);
            Field(typeof(TOP.Admin.AdminPanel), "status").SetValue(admin, "pending");
            var received = typeof(TOP.Admin.AdminPanel).GetMethod("OnGenerationCompleted", BindingFlags.Instance | BindingFlags.NonPublic);
            received.Invoke(admin, new object[] { 41, true, "old success" });
            check((string)Field(typeof(TOP.Admin.AdminPanel), "status").GetValue(admin) == "pending",
                "Late acknowledgement cannot confirm another generation request");
            received.Invoke(admin, new object[] { 42, true, "current success" });
            check((string)Field(typeof(TOP.Admin.AdminPanel), "status").GetValue(admin) == "current success",
                "Matching generation acknowledgement updates the panel");
            ui.Confirm("Validation", () => { });
            var confirm = ui.Canvas.transform.Find("Confirm");
            check(confirm != null && confirm.Find("WindowClose") != null, "Confirmation dialog also has X");
        }
        catch (Exception e)
        {
            check(false, e.ToString());
        }
        finally
        {
            Field(typeof(GuildUI), "instance", true).SetValue(null, oldGuild);
            Field(typeof(FriendsUI), "instance", true).SetValue(null, oldFriends);
            Field(typeof(QuestLogUI), "instance", true).SetValue(null, oldQuests);
            Field(typeof(PartyUI), "instance", true).SetValue(null, oldParty);
            Field(typeof(MailUI), "instance", true).SetValue(null, oldMail);
            Field(typeof(TradeUI), "instance", true).SetValue(null, oldTrade);
            UnityEngine.Object.DestroyImmediate(root);
            if (createdEvents != null) UnityEngine.Object.DestroyImmediate(createdEvents.gameObject);
        }
        report.AppendLine($"Passed={passed} Failed={failed}");
        File.WriteAllText("Tools/window-controls-validation-results.txt", report.ToString());
        Debug.Log("[WindowControlsValidation] " + $"Passed={passed} Failed={failed}");
    }
}
