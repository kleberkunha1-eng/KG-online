using UnityEngine;
using Mirror;
namespace TOP.Player
{
    public class PlayerRespawn : NetworkBehaviour
    {
        [SerializeField] float respawnDelay = 5f;
        [SerializeField] Vector3[] respawnPoints;
        bool waiting;
        [Server] public void Die() { GetComponent<PlayerController>().Die(); }
        [Server] public void ScheduleRespawn()
        {
            if (waiting) return;
            waiting = true;
            GetComponent<PlayerCombat>()?.StopAttack();
            GetComponent<PlayerMovement>()?.Stop();
            Invoke(nameof(RespawnPlayer), respawnDelay);
        }
        [Server] void RespawnPlayer()
        {
            waiting = false;
            GetComponent<PlayerController>().RespawnPlayer();
        }
    }
}
