using System;
using UnityEngine;

namespace TOP.Data
{
    [Serializable]
    public class StartCity
    {
        public int id; public string name, map, description;
        public float x, y, z, rotY; public bool enabled;
        public Vector3 Position => new Vector3(x, y, z);
    }

    [Serializable] class StartCityFile { public StartCity[] cities = new StartCity[0]; }

    /// <summary>Cidades iniciais disponiveis na criacao de personagem. Dados em Resources/Config/start_cities.json.</summary>
    public static class StartCities
    {
        static StartCity[] all;
        public static StartCity[] All
        {
            get
            {
                if (all == null)
                {
                    var asset = Resources.Load<TextAsset>("Config/start_cities");
                    all = asset != null ? JsonUtility.FromJson<StartCityFile>(asset.text).cities : new StartCity[0];
                }
                return all;
            }
        }
        public static StartCity Get(int id) { foreach (var c in All) if (c.id == id) return c; return null; }
    }
}