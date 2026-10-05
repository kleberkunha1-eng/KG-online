using System;
using UnityEngine;

namespace TOP.Data
{
    [Serializable]
    public class CharacterPreviewData
    {
        public long Id;
        public byte SlotIndex;
        public string Name;
        public byte Gender;
        public byte Job;
        public int Level;
        public string MapName;
        public float PosX;
        public float PosY;
        public float PosZ;
        public float RotationY;
        public byte HairStyle;
        public byte HairColor;
        public byte FaceStyle;
        public int[] Equipped = new int[0]; // item ids currently worn (shown on the selection screen)
        public DateTime? LastOnline;

        public Vector3 Position => new Vector3(PosX, PosY, PosZ);

        public void SetPosition(Vector3 pos)
        {
            PosX = pos.x;
            PosY = pos.y;
            PosZ = pos.z;
        }
    }
}
