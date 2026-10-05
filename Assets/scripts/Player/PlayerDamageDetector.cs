using UnityEngine;
using Mirror;

/// <summary>
/// Adicione este script no PLAYER para diagnosticar de onde vem a morte.
/// Ele loga TODAS as colisões e triggers que o player entra.
/// </summary>
namespace TOP.Player
{       
public class PlayerDamageDetector : NetworkBehaviour
{
    private Vector3 lastPosition;
    private float lastY;
    private float checkTimer;

    void Start()
    {
        lastPosition = transform.position;
        lastY = transform.position.y;
        Debug.Log($"[DamageDetector] Iniciado em {gameObject.name} | pos={transform.position}", this);
    }

    void Update()
    {
        checkTimer += Time.deltaTime;

        // Checa a cada 0.1s para não floodar
        if (checkTimer < 0.1f) return;
        checkTimer = 0f;

        // Detecta queda súbita (possível kill zone / void)
        float yDelta = transform.position.y - lastY;
        if (yDelta < -2f) // Caindo rápido
        {
            Debug.LogWarning($"[DamageDetector] {gameObject.name} CAINDO! yDelta={yDelta:F2} | pos={transform.position} | lastY={lastY:F2}", this);
        }

        // Detecta teleporte súbito (respawn)
        float posDelta = Vector3.Distance(transform.position, lastPosition);
        if (posDelta > 5f && Time.time > 2f)
        {
            Debug.LogWarning($"[DamageDetector] {gameObject.name} TELEPORTE! delta={posDelta:F2} | de={lastPosition} | para={transform.position}", this);
        }

        lastPosition = transform.position;
        lastY = transform.position.y;
    }

    void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[DamageDetector] TRIGGER ENTER: {gameObject.name} -> {other.name} (tag={other.tag}, layer={LayerMask.LayerToName(other.gameObject.layer)})", this);

        string tagLower = other.tag.ToLower();
        if (tagLower.Contains("kill") || tagLower.Contains("void") || tagLower.Contains("death") || tagLower.Contains("damage"))
        {
            Debug.LogError($"[DamageDetector] >>> KILL ZONE DETECTADA! {other.name} at {other.transform.position} <<<", this);
        }
    }

    void OnTriggerExit(Collider other)
    {
        Debug.Log($"[DamageDetector] TRIGGER EXIT: {gameObject.name} -> {other.name}", this);
    }

    void OnCollisionEnter(Collision collision)
    {
        Debug.Log($"[DamageDetector] COLLISION: {gameObject.name} -> {collision.gameObject.name} (tag={collision.gameObject.tag})", this);
    }
}
}