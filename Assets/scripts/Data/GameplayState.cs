using System;
using System.Collections.Generic;
namespace TOP.Data
{
    [Serializable] public class GameplayState
    {
        public int Version = 1;
        public List<FairyState> Fairies = new List<FairyState>();
        public List<StallOffer> Offers = new List<StallOffer>();
        public string StallName = "";
    }
    [Serializable] public class FairyState
    {
        public string ItemKey;
        public int Growth, Stamina = 5000, Strength, Agility, Accuracy, Constitution, Spirit;
        public int Level => Strength + Agility + Accuracy + Constitution + Spirit;
    }
    [Serializable] public class StallOffer
    {
        public string ItemKey;
        public int ItemId, Quantity;
        public long Price;
    }
}
