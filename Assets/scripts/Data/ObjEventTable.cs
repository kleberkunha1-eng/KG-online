using System.Collections.Generic;

namespace TOP.Data
{
    public enum ObjEventKind { JumpPoint, ShipDock, Investigate, JumpPointSea }

    public class ObjEventDef
    {
        public int Id;
        public string Name;
        public ObjEventKind Kind;
        public bool ClickToTrigger; // true = clique (reunir), false = por raio de proximidade
        public float TriggerRadius;
        public int ValidFor; // 0 = todos, 1 = apenas jogador a pe, 2 = apenas navio
    }

    // Tabela extraida do cliente original (scripts/table/objevent.txt). Define os 4 tipos de
    // objeto interativo de mundo usados pelo jogo (pontos de salto/teleporte, ancoradouro de
    // navio e eventos de investigação). As posicoes reais de cada instancia no mapa estao
    // compiladas nos arquivos de mapa binarios do cliente original e nao sao legiveis como
    // texto; o componente WorldEventObject abaixo fornece a infraestrutura funcional completa
    // para que essas instancias sejam posicionadas livremente nas cenas Unity.
    public static class ObjEventTable
    {
        public static readonly Dictionary<int, ObjEventDef> ById = new Dictionary<int, ObjEventDef>
        {
            [1] = new ObjEventDef { Id = 1, Name = "Jump Point", Kind = ObjEventKind.JumpPoint, ClickToTrigger = true, TriggerRadius = 2f, ValidFor = 0 },
            [2] = new ObjEventDef { Id = 2, Name = "Ship Dock", Kind = ObjEventKind.ShipDock, ClickToTrigger = true, TriggerRadius = 2f, ValidFor = 2 },
            [3] = new ObjEventDef { Id = 3, Name = "Investigate Event", Kind = ObjEventKind.Investigate, ClickToTrigger = true, TriggerRadius = 0f, ValidFor = 0 },
            [4] = new ObjEventDef { Id = 4, Name = "Jump Point (Sea)", Kind = ObjEventKind.JumpPointSea, ClickToTrigger = true, TriggerRadius = 2f, ValidFor = 2 },
        };
    }
}
