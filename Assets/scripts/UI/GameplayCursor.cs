using Mirror;
using TOP.Core;
using TOP.Data;
using TOP.Player;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TOP.UI
{
    public enum GameplayCursorKind { Default, Hand, Sword }

    public sealed class GameplayCursor : MonoBehaviour
    {
        static GameplayCursor instance;
        Texture2D hand, sword;
        GameplayCursorKind current;
        bool initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isBatchMode || instance != null) return;
            instance = new GameObject("GameplayCursor").AddComponent<GameplayCursor>();
            DontDestroyOnLoad(instance.gameObject);
        }

        void Awake()
        {
            hand = Resources.Load<Texture2D>("PKOCursors/mouseon");
            sword = Resources.Load<Texture2D>("PKOCursors/attack");
            if (hand == null || sword == null || !hand.isReadable || !sword.isReadable)
            {
                Debug.LogError("[Cursor] Original hand/sword textures missing or unreadable. Run the original cursor importer.");
                enabled = false;
            }
        }

        public static bool PointerOverUi() => GameWindowControls.BlocksMouse || TOP.Admin.AdminPanel.BlocksMouse
            || Pko.PkoUi.PointerOverWindow()
            || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());

        public static bool CanAttackPlayer(PlayerController local, PlayerController target)
        {
            if (local == null || target == null || local == target || local.CharacterId <= 0 || target.CharacterId <= 0
                || local.netId == 0 || target.netId == 0
                || local.CurrentHp <= 0 || target.CurrentHp <= 0 || local.MapName != target.MapName
                || local.BoatOperationPending || target.BoatOperationPending
                || (local.GetComponent<PlayerTrade>() != null && local.GetComponent<PlayerTrade>().InTrade)
                || (target.GetComponent<PlayerTrade>() != null && target.GetComponent<PlayerTrade>().InTrade))
                return false;
            var combat = local.GetComponent<PlayerCombat>();
            var other = target.GetComponent<PlayerCombat>();
            if (combat != null && other != null && combat.DuelOpponentNetId == target.netId
                && other.DuelOpponentNetId == local.netId && Vector3.Distance(local.transform.position, target.transform.position) <= 20)
                return true;
            if (local.ArenaInstanceId != target.ArenaInstanceId) return false;
            if (local.ArenaInstanceId > 0 && !TOP.Systems.ArenaCoordinator.Opponents(local, target)) return false;
            Vector3 sourcePosition = local.ArenaInstanceId > 0 ? TOP.Systems.ArenaWorld.AttributePosition(local.ArenaInstanceId, local.transform.position) : local.transform.position;
            Vector3 targetPosition = target.ArenaInstanceId > 0 ? TOP.Systems.ArenaWorld.AttributePosition(target.ArenaInstanceId, target.transform.position) : target.transform.position;
            PlayerParty sourceParty = local.GetComponent<PlayerParty>();
            PlayerParty targetParty = target.GetComponent<PlayerParty>();
            PlayerGuild sourceGuild = local.GetComponent<PlayerGuild>();
            PlayerGuild targetGuild = target.GetComponent<PlayerGuild>();
            bool sourceGuildKnown = !OriginalPvpRules.RequiresGuildData(local.MapName) || (sourceGuild != null && sourceGuild.GuildDataReady);
            bool targetGuildKnown = !OriginalPvpRules.RequiresGuildData(target.MapName) || (targetGuild != null && targetGuild.GuildDataReady);
            return OriginalPvpRules.CanFightInMap(local.MapName, sourcePosition,
                sourceParty != null ? sourceParty.PartyId : 0, sourceGuild != null ? sourceGuild.GuildId : 0,
                sourceGuildKnown, 0, target.MapName, targetPosition,
                targetParty != null ? targetParty.PartyId : 0, targetGuild != null ? targetGuild.GuildId : 0,
                targetGuildKnown, 0);
        }
        public static GameplayCursorKind Classify(Collider collider, PlayerController local)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy
                || local == null || local.CurrentHp <= 0) return GameplayCursorKind.Default;
            var enemy = collider.GetComponentInParent<EnemyStats>();
            if (enemy != null) return enemy.IsDead ? GameplayCursorKind.Default : GameplayCursorKind.Sword;
            var player = collider.GetComponentInParent<PlayerController>();
            if (player != null)
                return player == local ? GameplayCursorKind.Default : CanAttackPlayer(local, player)
                    ? GameplayCursorKind.Sword : GameplayCursorKind.Hand;
            var interactable = collider.GetComponentInParent<IInteractable>();
            if (interactable == null || (interactable is Behaviour behaviour && !behaviour.isActiveAndEnabled))
                return GameplayCursorKind.Default;
            return collider.GetComponentInParent<NetworkIdentity>() != null ? GameplayCursorKind.Hand : GameplayCursorKind.Default;
        }

        void LateUpdate()
        {
            var identity = NetworkClient.localPlayer;
            var local = identity != null ? identity.GetComponent<PlayerController>() : null;
            var movement = local != null ? local.GetComponent<PlayerMovement>() : null;
            var camera = Camera.main;
            var kind = GameplayCursorKind.Default;
            if (local != null && movement != null && movement.InputEnabled && camera != null && !PointerOverUi()
                && Physics.Raycast(camera.ScreenPointToRay(Input.mousePosition), out var hit, movement.PointerRaycastDistance))
                kind = Classify(hit.collider, local);
            if (initialized && current == kind) return;
            current = kind;
            initialized = true;
            Cursor.SetCursor(kind == GameplayCursorKind.Hand ? hand : kind == GameplayCursorKind.Sword ? sword : null,
                kind == GameplayCursorKind.Hand ? new Vector2(1, 1) : Vector2.zero, CursorMode.Auto);
        }

        void OnDisable() => Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        void OnDestroy() { if (instance == this) instance = null; }
    }
}
