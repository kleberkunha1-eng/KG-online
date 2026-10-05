// Gera functions/_items.json a partir do iteminfo original.
const fs = require('fs'), path = require('path');
const src = process.argv[2] || path.join(__dirname, '..', '..', 'API', 'data', 'iteminfo.txt');
const TYPES = { 1: 'Sword', 2: 'Two-handed Sword', 3: 'Bow', 4: 'Firearm', 5: 'Dagger', 6: 'Staff', 7: 'Cannon', 11: 'Shield', 20: 'Hat', 22: 'Armor', 23: 'Gloves', 24: 'Boots', 25: 'Necklace', 26: 'Ring', 27: 'Tattoo', 44: 'Wings', 59: 'Pet' };
const out = [];
for (const line of fs.readFileSync(src, 'latin1').split(/\r?\n/)) {
  if (!line || line.startsWith('//')) continue;
  const c = line.split('\t'); const id = parseInt(c[0], 10);
  if (!id || c.length < 30 || !c[1]) continue;
  out.push({ id, name: c[1], type: TYPES[parseInt(c[10], 10)] || 'Item', rarity: 'Normal', level: parseInt(c[24], 10) || 1, description: (c[c.length - 2] || '').trim(), image: '/placeholder.svg' });
}
fs.writeFileSync(path.join(__dirname, '..', 'functions', '_items.json'), JSON.stringify(out));
console.log(out.length + ' itens');