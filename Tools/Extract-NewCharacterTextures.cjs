const fs = require('node:fs');
const path = require('node:path');

const source = path.resolve(__dirname, '..', 'Assets', 'ImportedClient', 'NewCharacterTest', 'Personagem_RPG.glb');
const data = fs.readFileSync(source);
if (data.readUInt32LE(0) !== 0x46546c67 || data.readUInt32LE(4) !== 2) throw new Error('Invalid GLB.');
const jsonLength = data.readUInt32LE(12);
const document = JSON.parse(data.subarray(20, 20 + jsonLength).toString());
const offset = 20 + jsonLength;
if (data.readUInt32LE(offset + 4) !== 0x004e4942) throw new Error('Missing GLB binary chunk.');
if (document.materials.length !== 1) throw new Error('Expected one character material.');
const material = document.materials[0];
const textures = {
    baseColor: material.pbrMetallicRoughness.baseColorTexture,
    normal: material.normalTexture,
    metallicRoughness: material.pbrMetallicRoughness.metallicRoughnessTexture,
};
const output = path.join(path.dirname(source), 'Textures');
fs.mkdirSync(output, { recursive: true });
for (const [name, texture] of Object.entries(textures)) {
    if (!texture) throw new Error('Missing texture ' + name);
    const image = document.images[document.textures[texture.index].source];
    if (image.mimeType !== 'image/png' || image.bufferView === undefined) throw new Error('Expected embedded PNG for ' + name);
    const view = document.bufferViews[image.bufferView];
    const start = offset + 8 + (view.byteOffset || 0);
    const bytes = data.subarray(start, start + view.byteLength);
    if (bytes.length !== view.byteLength) throw new Error('Incomplete image ' + name);
    const target = path.join(output, name + '.png');
    fs.writeFileSync(target, bytes);
    console.log('Extracted ' + name + ': ' + bytes.length + ' bytes');
}
