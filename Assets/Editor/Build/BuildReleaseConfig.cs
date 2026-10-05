/// <summary>
/// Configuracao central do pipeline de release, sem nenhum segredo (certificado, senha,
/// credenciais do Azure). Esses valores ficam sempre fora do projeto, em variaveis de
/// ambiente (ver Docs/CodeSigning.md) - nunca aqui.
/// </summary>
public static class BuildReleaseConfig
{
    public const string ProductName = "GameProjectKG";
    public const string PublisherDisplayName = "KG Studios";
    public const string OutputDirectory = "Build/GameProjectKG";
    public const string ReleaseDirectory = "Release";
    public const string InstallerOutputDirectory = "Release";

    /// <summary>Canal do build: Development (sem exigir assinatura) ou Release (deve assinar).</summary>
    public enum Channel { Development, Release }
}
