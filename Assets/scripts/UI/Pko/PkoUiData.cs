using System;

namespace TOP.UI.Pko
{
    [Serializable] public class PkoImg { public string s, t; public int w, h, u, v; }

    [Serializable]
    public class PkoComp
    {
        public int id; public string name, type; public int x, y, w, h; public bool show;
        public string caption, hint; public PkoImg[] imgs = new PkoImg[0];
        public int tag; public long textColor; public int cols, rows, cellW, cellH, gapX, gapY, maxNum, group, page, tabIndex, tabKey;
    }

    [Serializable]
    public class PkoForm
    {
        public string file, name; public int w, h, x, y; public bool show, drag, esc;
        public string hotMod, hotKey, style; public PkoComp[] comps = new PkoComp[0];
    }

    // Ajustes opcionais em Resources/PKOUI/overrides.json: reposicionar, trocar texto/textura ou esconder qualquer form/componente sem tocar no codigo.
    [Serializable] public class PkoCompOverride { public string name; public bool hidden, hasPos, hasSize; public int x, y, w, h; public string caption, texture; }
    [Serializable] public class PkoFormOverride { public string name, file; public bool hasPos; public int x, y; public PkoCompOverride[] comps = new PkoCompOverride[0]; }
    [Serializable] public class PkoOverridesFile { public PkoFormOverride[] forms = new PkoFormOverride[0]; }
    [Serializable] public class PkoFormsFile { public PkoForm[] forms = new PkoForm[0]; }
}
