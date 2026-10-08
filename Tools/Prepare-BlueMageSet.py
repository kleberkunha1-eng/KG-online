import argparse
import json
from pathlib import Path

import bpy
from mathutils import Vector


def prepare(source, project):
    output = project / "Assets" / "ImportedClient" / "BlueMageSet"
    icons = project / "Assets" / "Resources" / "PKOUI" / "icon"
    output.mkdir(parents=True, exist_ok=True)
    icons.mkdir(parents=True, exist_ok=True)
    report = []
    for name in ("helm", "chestplate", "gloves", "pants", "boots"):
        files = [p for p in source.glob("*.obj") if name in p.name.lower()]
        if len(files) != 1:
            raise ValueError(f"Expected one {name} OBJ, found {len(files)}")
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.object.delete(use_global=False)
        bpy.ops.wm.obj_import(filepath=str(files[0]))
        objects = [o for o in bpy.context.scene.objects if o.type == "MESH"]
        for obj in objects:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = objects[0]
        if len(objects) > 1:
            bpy.ops.object.join()
        obj = bpy.context.object
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        original = len(obj.data.polygons)
        decimate = obj.modifiers.new("Game mesh", "DECIMATE")
        decimate.ratio = min(1.0, 10000 / original)
        bpy.ops.object.modifier_apply(modifier=decimate.name)
        mesh = obj.data
        mesh.calc_loop_triangles()
        if len(mesh.loop_triangles) > 22000:
            raise ValueError(f"{name}: triangle budget exceeded")
        texture_path = files[0].with_suffix(".png")
        image = bpy.data.images.load(str(texture_path), check_existing=True)
        image.scale(2048, 2048)
        image.filepath_raw = str(output / f"{name}.png")
        image.file_format = "PNG"
        image.save()
        material = bpy.data.materials.new(f"BlueMage_{name}")
        material.use_nodes = True
        bsdf = material.node_tree.nodes.get("Principled BSDF")
        tex = material.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = image
        material.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Metallic"].default_value = 0.25
        bsdf.inputs["Roughness"].default_value = 0.45
        mesh.materials.clear()
        mesh.materials.append(material)
        # Keep split UV vertices: welding by position alone destroys texture seams.
        vertices, normals, uvs, triangles = [], [], [], []
        lookup = {}
        for tri in mesh.loop_triangles:
            for loop_index in tri.loops:
                loop = mesh.loops[loop_index]
                vertex = mesh.vertices[loop.vertex_index]
                uv = mesh.uv_layers.active.data[loop_index].uv
                key = (loop.vertex_index, round(uv.x, 7), round(uv.y, 7))
                if key not in lookup:
                    lookup[key] = len(vertices) // 3
                    vertices.extend((vertex.co.x, vertex.co.z, -vertex.co.y))
                    normals.extend((vertex.normal.x, vertex.normal.z, -vertex.normal.y))
                    uvs.extend(uv)
                triangles.append(lookup[key])
        (output / f"{name}.json").write_text(json.dumps(
            dict(positions=vertices, normals=normals, uvs=uvs, triangles=triangles),
            separators=(",", ":")), encoding="utf-8")
        scene = bpy.context.scene
        scene.render.engine = "BLENDER_EEVEE_NEXT"
        scene.render.resolution_x = scene.render.resolution_y = 256
        scene.render.resolution_percentage = 100
        scene.render.film_transparent = True
        scene.world.color = (0.3, 0.3, 0.3)
        bounds = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
        center = sum(bounds, Vector()) / 8
        extent = max(max(v[i] for v in bounds) - min(v[i] for v in bounds) for i in range(3))
        bpy.ops.object.camera_add(location=center + Vector((0.25, -2.8, 0.8)) * extent)
        camera = bpy.context.object
        camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
        camera.data.type = "ORTHO"
        camera.data.ortho_scale = extent * 1.20
        camera.data.lens = 50
        scene.camera = camera
        for offset, energy, size in [((-2, -3, 4), 700, 4), ((3, -1, 2), 450, 3), ((0, 2, 3), 600, 2)]:
            bpy.ops.object.light_add(type="AREA", location=center + Vector(offset) * extent)
            light = bpy.context.object
            light.data.energy = energy * extent * extent
            light.data.shape = "DISK"
            light.data.size = size * extent
            light.rotation_euler = (center - light.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = str(icons / f"bluemage_{name}.png")
        bpy.ops.render.render(write_still=True)
        report.append(dict(piece=name, source=files[0].name, original_faces=original, triangles=len(triangles) // 3,
                           vertices=len(vertices) // 3))
        print("BLUE_SET", report[-1], flush=True)
    (output / "source-manifest.json").write_text(json.dumps(report, indent=2), encoding="utf-8")


if __name__ == "__main__":
    import sys
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--project", type=Path, required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    prepare(args.source, args.project)
