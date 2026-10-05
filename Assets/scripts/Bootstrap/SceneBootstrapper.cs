using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// SceneBootstrapper - Carrega LoginScene automaticamente quando o jogo inicia
/// </summary>
public class SceneBootstrapper : MonoBehaviour
{
    void Awake()
    {
        // Garante que este objeto persiste entre cenas
        DontDestroyOnLoad(gameObject);

        // Se não estamos na LoginScene, carrega ela
        if (SceneManager.GetActiveScene().name != "LoginScene")
        {
            Debug.Log("[SceneBootstrapper] Carregando LoginScene...");
            SceneManager.LoadScene("LoginScene", LoadSceneMode.Single);
        }
        else
        {
            Debug.Log("[SceneBootstrapper] LoginScene já está carregada!");
        }
    }
}
