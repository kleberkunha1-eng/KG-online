using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TOP.Data
{
    // StringSet.txt: tabela de textos originais do cliente (uma entrada por id numerico, em ingles).
    // Usada por varias tabelas (skillinfo, iteminfo, msg de sistema) que referenciam textos por id
    // em vez de guardar a string diretamente. Disponibiliza Get(id) para qualquer sistema que precise
    // do texto original (quests, mensagens de sistema, etc.).
    public static class PkoStrings
    {
        static readonly Encoding Latin1 = System.Text.Encoding.GetEncoding(28591);
        static readonly Regex Line = new Regex(@"^\[(\d+)\]\s*""(.*)""\s*$");
        static Dictionary<int, string> _map;

        public static IReadOnlyDictionary<int, string> All { get { if (_map == null) Load(); return _map; } }

        public static string Get(int id, string fallback = "") => All.TryGetValue(id, out var s) ? s : fallback;

        static void Load()
        {
            _map = new Dictionary<int, string>();
            var asset = Resources.Load<TextAsset>("PKO/StringSet");
            if (asset == null) { Debug.LogWarning("[PkoStrings] StringSet.txt ausente em Resources/PKO."); return; }
            string text = Latin1.GetString(asset.bytes);
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                var m = Line.Match(line);
                if (!m.Success) continue;
                if (int.TryParse(m.Groups[1].Value, out var id))
                    _map[id] = m.Groups[2].Value.Replace("\\n", "\n").Replace("\\\"", "\"");
            }
        }
    }
}
