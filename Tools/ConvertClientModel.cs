using System;
using System.IO;
using System.Globalization;
using System.Drawing;
using System.Drawing.Imaging;
using Mindpower;

class ConvertClientModel
{
    static string Root;
    static string Cut(char[] chars) { return new string(chars).Split('\0')[0]; }
    static Bitmap Decode(byte[] bytes)
    {
        try { using(var stream = new MemoryStream(bytes)) return new Bitmap(stream); }
        catch { try { using(var stream = new MemoryStream(bytes)) return FreeImageAPI.FreeImageBitmap.FromStream(stream).ToBitmap(); } catch { return null; } }
    }
    static string Texture(string name, string output)
    {
        string source = Path.Combine(Root, "texture", "scene", name);
        if(!File.Exists(source)) { string folder=Path.Combine(Root,"texture","scene"); string stem=Path.GetFileNameWithoutExtension(name); string[] matches=Directory.GetFiles(folder,stem+".*"); if(matches.Length>0) source=matches[0]; else { Console.WriteLine("MISSING_TEXTURE "+name); return null; } }
        string target = Path.Combine(output,Path.GetFileNameWithoutExtension(name)+".png");
        if(File.Exists(target)) return Path.GetFileName(target);
        byte[] bytes=File.ReadAllBytes(source); var bitmap=Decode(bytes);
        if(bitmap == null && bytes.Length >= 88)
        {
            var decoded=new byte[bytes.Length]; Array.Copy(bytes,bytes.Length-48,decoded,0,44);
            Array.Copy(bytes,44,decoded,44,bytes.Length-88); Array.Copy(bytes,0,decoded,bytes.Length-44,44);
            bitmap=Decode(decoded);
        }
        if(bitmap==null) { Console.WriteLine("TEXTURE_FAILED "+source); return null; }
        using(bitmap) { bitmap.Save(target,ImageFormat.Png); }
        return Path.GetFileName(target);
    }
    static int Main(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        Root=args[0]; string name=args[1], output=args[2]; Directory.CreateDirectory(output);
        var model=new lwModelObjInfo();
        if(model.Load(Path.Combine(Root,"model","scene",name+".lmo"))<0) return 1;
        using(var obj=new StreamWriter(Path.Combine(output,name+".obj")))
        using(var mtl=new StreamWriter(Path.Combine(output,name+".mtl")))
        {
            obj.WriteLine("mtllib "+name+".mtl"); int offset=1;
            for(int g=0;g<model.geom_obj_num;g++)
            {
                var geom=model.geom_obj_seq[g]; var mesh=geom.mesh;
                obj.WriteLine("o part_"+g);
                foreach(var v in mesh.vertex_seq) { var p=v*geom.header.mat_local; obj.WriteLine("v {0} {1} {2}",p.x,p.z,p.y); }
                for(int i=0;i<mesh.vertex_seq.Length;i++) { var uv=mesh.texcoord0_seq[i]; obj.WriteLine("vt {0} {1}",uv.x,1-uv.y); }
                for(int m=0;m<mesh.subset_seq.Length;m++)
                {
                    string mat="material_"+g+"_"+m; mtl.WriteLine("newmtl "+mat); mtl.WriteLine("Kd 1 1 1\nd 1\nillum 1");
                    if(m<geom.mtl_num) { var texture=Texture(Cut(geom.mtl_seq[m].tex_seq[0].file_name),output); if(texture!=null) mtl.WriteLine("map_Kd "+texture); }
                    obj.WriteLine("usemtl "+mat); var sub=mesh.subset_seq[m];
                    for(uint i=sub.start_index;i<sub.start_index+sub.primitive_num*3;i+=3)
                    {
                        long a=offset+mesh.index_seq[i], b=offset+mesh.index_seq[i+2], c=offset+mesh.index_seq[i+1];
                        obj.WriteLine("f {0}/{0} {1}/{1} {2}/{2}",a,b,c);
                    }
                }
                offset+=mesh.vertex_seq.Length;
            }
        }
        Console.WriteLine(name+": "+model.geom_obj_num+" meshes converted"); return 0;
    }
}
