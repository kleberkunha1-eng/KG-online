using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace TOP.EditorTools
{
    public static class GarnerTerrainImporter
    {
        // The original map Y axis grows southwards, so Unity Z = OriginZ - y.
        public const int OriginX=2218, OriginZ=2782;
        public static GameObject Build() => BuildTile("Garner_Argent","Assets/ImportedClient/GarnerArgent.asset",1706,2270,1024,true);
        public static GameObject BuildTile(string objName,string dataPath,int StartX,int StartZ,int Size,bool writeReport=false)
        {
            var bytes=File.ReadAllBytes(@"E:\NEW SV\Client\map\garner.map");
            int version=BitConverter.ToInt32(bytes,0), width=BitConverter.ToInt32(bytes,4), height=BitConverter.ToInt32(bytes,8);
            int sw=BitConverter.ToInt32(bytes,12), sh=BitConverter.ToInt32(bytes,16);
            if(version!=780627 || width!=4096 || height!=4096 || sw!=8 || sh!=8) throw new InvalidDataException("Unsupported Garner map header");
            int At(int x,int z)
            {
                x=Math.Min(x,width-1); z=Math.Min(z,height-1); int section=z/sh*(width/sw)+x/sw;
                int offset=BitConverter.ToInt32(bytes,20+section*4);
                if(offset==0) return -1;
                int p=offset+((z%sh)*sw+x%sw)*15;
                if(p<20 || p+15>bytes.Length) throw new InvalidDataException("Invalid map section offset");
                return p;
            }
            var table=new Dictionary<int,string>();
            foreach(var line in File.ReadAllLines(@"E:\NEW SV\Client\scripts\table\TerrainInfo.txt"))
            {
                var cols=line.Split(new[]{'\t',' '},StringSplitOptions.RemoveEmptyEntries);
                if(cols.Length>1 && int.TryParse(cols[0],out var id)) table[id]=Path.GetFileNameWithoutExtension(cols[1]);
            }
            var ids=new SortedSet<int>();
            for(int z=0;z<Size;z++) for(int x=0;x<Size;x++) { int p=At(StartX+x,StartZ+z); if(p>=0) { ids.Add(bytes[p+4]); uint info=BitConverter.ToUInt32(bytes,p); foreach(int shift in new[]{26,16,6}) { int id=(int)((info>>shift)&63); if(id>0) ids.Add(id); } } }
            var valid=ids.Where(id=>table.ContainsKey(id) && File.Exists("Assets/ImportedClient/Terrain/"+table[id]+".png")).ToArray();
            if(valid.Length==0) throw new Exception("No converted terrain textures");
            var layers=new TerrainLayer[valid.Length];
            for(int i=0;i<valid.Length;i++)
            {
                string path="Assets/ImportedClient/Terrain/Layer_"+valid[i]+".terrainlayer";
                var layer=AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if(layer==null) { layer=new TerrainLayer(); AssetDatabase.CreateAsset(layer,path); }
                layer.diffuseTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ImportedClient/Terrain/"+table[valid[i]]+".png");
                layer.tileSize=new Vector2(4,4); layer.smoothness=0; layers[i]=layer; EditorUtility.SetDirty(layer);
            }
            var data=AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
            if(data==null) { data=new TerrainData(); AssetDatabase.CreateAsset(data,dataPath); }
            data.heightmapResolution=Size+1; data.alphamapResolution=Size; data.size=new Vector3(Size,32,Size); data.terrainLayers=layers;
            var heights=new float[Size+1,Size+1];
            for(int z=0;z<=Size;z++) for(int x=0;x<=Size;x++) { int p=At(StartX+x,StartZ+Size-z); heights[z,x]=(p<0 ? 6f : (sbyte)bytes[p+7]*.1f+8f)/32f; }
            data.SetHeights(0,0,heights);
            var splat=new float[Size,Size,layers.Length];
            for(int z=0;z<Size;z++) for(int x=0;x<Size;x++)
            {
                int p=At(StartX+x,StartZ+Size-1-z); int id=p<0?valid[0]:bytes[p+4]; int idx=Array.IndexOf(valid,id); if(idx<0) idx=0; splat[z,x,idx]=1;
                if(p<0) continue; uint info=BitConverter.ToUInt32(bytes,p);
                foreach(int shift in new[]{26,16,6})
                {
                    int overlay=(int)((info>>shift)&63); int alpha=(int)((info>>(shift-4))&15); int target=Array.IndexOf(valid,overlay);
                    if(target<0 || alpha==0) continue;
                    int bits=0; for(int b=0;b<4;b++) bits+=(alpha>>b)&1; float amount=bits/4f;
                    for(int k=0;k<layers.Length;k++) splat[z,x,k]*=1-amount;
                    splat[z,x,target]+=amount;
                }
            }
            data.SetAlphamaps(0,0,splat); EditorUtility.SetDirty(data);
            var previous=GameObject.Find(objName); if(previous!=null) UnityEngine.Object.DestroyImmediate(previous);
            var go=Terrain.CreateTerrainGameObject(data); go.name=objName; go.layer=LayerMask.NameToLayer("Terrain");
            go.transform.position=new Vector3(StartX-OriginX,-8,OriginZ-StartZ-Size);
            var terrain=go.GetComponent<Terrain>(); terrain.drawInstanced=true; terrain.heightmapPixelError=3;
            string materialPath="Assets/ImportedClient/GarnerTerrain.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(material==null) { material=new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit")); AssetDatabase.CreateAsset(material,materialPath); }
            terrain.materialTemplate=material;
            if(writeReport) File.WriteAllText("Tools/garner-import-report.txt",$"Original: garner.map v{version}, {width}x{height}\nImported region: ({StartX},{StartZ}) to ({StartX+Size},{StartZ+Size})\nUnity origin corresponds to original ({OriginX},{OriginZ}).\nLayers: {valid.Length}. Heights preserved; tile-edge alpha masks approximated by terrain splat weights.\nScenery comes from the original garner.obj. Unity Z = OriginZ - y (the original Y axis grows southwards).\n");
            return go;
        }
    }
}

