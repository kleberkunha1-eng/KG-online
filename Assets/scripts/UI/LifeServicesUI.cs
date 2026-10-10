using System.Linq;
using Mirror;
using TOP.Data;
using TOP.Player;
using UnityEngine;
namespace TOP.UI
{
    public class LifeServicesUI : MonoBehaviour
    {
        static LifeServicesUI instance;
        PlayerLifeServices local;
        bool open;
        Rect window = new Rect(250, 100, 520, 500);
        string slot = "0", quantity = "1", price = "100", name = "Minha barraca";
        Vector2 scroll;
        public static void Bind(PlayerLifeServices owner)
        {
            if (instance == null) { var go = new GameObject("LifeServicesUI"); DontDestroyOnLoad(go); instance = go.AddComponent<LifeServicesUI>(); }
            instance.local = owner;
        }
        void Update() { if (local != null && Input.GetKeyDown(KeyCode.F9)) open = !open; }
        void OnGUI()
        {
            if (local == null) return;
            if (GUI.Button(new Rect(Screen.width - 150, 35, 140, 24), "Fairy / Barraca (F9)")) open = !open;
            foreach (var id in NetworkClient.spawned.Values)
            {
                var guild = id.GetComponent<PlayerGuild>();
                if (guild != null && !string.IsNullOrEmpty(guild.GuildName) && Camera.main != null)
                {
                    var guildAt = Camera.main.WorldToScreenPoint(guild.transform.position + Vector3.up * 3.35f);
                    if (guildAt.z > 0)
                    {
                        var previous = GUI.color; GUI.color = new Color(0.3f, 1f, 0.65f);
                        GUI.Label(new Rect(guildAt.x - 100, Screen.height - guildAt.y, 200, 24), "[" + guild.GuildName + "]");
                        GUI.color = previous;
                    }
                }
                var stall = id.GetComponent<PlayerLifeServices>();
                if (stall == null || !stall.StallActive || Camera.main == null) continue;
                var at = Camera.main.WorldToScreenPoint(stall.transform.position + Vector3.up * 3);
                if (at.z > 0) GUI.Label(new Rect(at.x - 100, Screen.height - at.y, 200, 24), "[Barraca] " + stall.StallName);
            }
            if (open) window = GameWindowControls.Window(7768, window, Draw, "Fairy / Barracas", () => open = false);
        }
        void Draw(int id)
        {
            GUILayout.Space(24);
            GUILayout.Label("Fairy equipada: nivel " + local.FairyLevel + " | stamina " + local.FairyStamina + " | growth " + local.FairyGrowth + "/" + PlayerLifeServices.GrowthCap(local.FairyLevel));
            GUILayout.BeginHorizontal(); GUILayout.Label("Slot comida / item (0-based)"); slot = GUILayout.TextField(slot, GUILayout.Width(60));
            if (GUILayout.Button("Alimentar fairy") && int.TryParse(slot, out int feed)) local.CmdFeedFairy(feed);
            GUILayout.EndHorizontal();
            GUILayout.Label("Barraca: exige Set Stall (241). Reservas persistem; vendedor deve estar online.");
            name = GUILayout.TextField(name);
            GUILayout.BeginHorizontal(); GUILayout.Label("Quantidade"); quantity = GUILayout.TextField(quantity, GUILayout.Width(70)); GUILayout.Label("Preco unitario"); price = GUILayout.TextField(price, GUILayout.Width(110)); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Adicionar oferta") && int.TryParse(slot, out int item) && int.TryParse(quantity, out int n) && long.TryParse(price, out long gold)) local.CmdSetStall(name, item, n, gold);
            if (GUILayout.Button("Fechar barraca")) local.CmdCloseStall();
            GUILayout.EndHorizontal();
            GUILayout.Label("Barracas proximas (compra de 1 unidade):");
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var identity in NetworkClient.spawned.Values)
            {
                var seller = identity.GetComponent<PlayerLifeServices>();
                if (seller == null || seller == local || !seller.StallActive || Vector3.Distance(local.transform.position, seller.transform.position) > 5) continue;
                var state = JsonUtility.FromJson<GameplayState>(seller.StallData);
                if (state == null) continue;
                foreach (var offer in state.Offers)
                {
                    string label = PkoTables.Items.TryGetValue(offer.ItemId, out var def) ? def.Name : offer.ItemId.ToString();
                    if (GUILayout.Button(seller.StallName + " | " + label + " x" + offer.Quantity + " | " + offer.Price + " ouro")) local.CmdBuyStall(identity.netId, offer.ItemKey, 1);
                }
            }
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, 520, 20));
        }
    }
}
